using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace GeoClubBot.ApiProbe;

/// <summary>
/// Listens on GeoGuessr's WebSocket to a club's topic, the way the bot's socket does, and reports
/// every frame plus a field census per code at the end. It exists because the club frames the bot
/// relies on (<c>ClubMemberJoined</c>, <c>ClubMemberLeft</c>, <c>ClubUpdated</c>,
/// <c>ClubMissionBoardUpdated</c>) were taken from GeoGuessr's client code, not observed: run it while
/// someone joins, leaves or claims a mission, and compare with <c>Documentation/GeoGuessrSocket.md</c>.
/// </summary>
/// <remarks>Sends nothing but what <see cref="ReadOnlySocketGuard"/> allows.</remarks>
public static class SocketProbe
{
    private const string DefaultUrl = "wss://api.geoguessr.com/ws";
    private const string DefaultClientVersion = "web-1.8700-39ce1da";
    private const string UrlEnvironmentVariable = "GEOGUESSR_SOCKET_URL";

    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan KeepAlive = TimeSpan.FromSeconds(20);

    public static async Task RunAsync(
        string ncfaToken, Guid clubId, TimeSpan? duration, string? clientVersion, Action<string> emit, CancellationToken cancellationToken)
    {
        using var listening = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (duration is { } limit)
        {
            listening.CancelAfter(limit);
        }

        using var socket = new ClientWebSocket();
        socket.Options.SetRequestHeader("Cookie", $"_ncfa={ncfaToken}");
        socket.Options.SetRequestHeader("Origin", "https://www.geoguessr.com");
        socket.Options.SetRequestHeader("User-Agent", "GeoClubBot.ApiProbe (+https://github.com/efibs/GeoClubBot)");
        socket.Options.KeepAliveInterval = KeepAlive;
        socket.Options.KeepAliveTimeout = KeepAlive;
        socket.Options.CollectHttpResponseDetails = true;

        var baseUrl = Environment.GetEnvironmentVariable(UrlEnvironmentVariable) is { Length: > 0 } custom ? custom : DefaultUrl;
        var query = $"c={Uri.EscapeDataString(clientVersion ?? DefaultClientVersion)}&tabId={Guid.NewGuid()}&attempt=1&visibility=visible";
        var url = new UriBuilder(baseUrl) { Query = query }.Uri;

        try
        {
            await socket.ConnectAsync(url, listening.Token).ConfigureAwait(false);
        }
        catch (WebSocketException) when (socket.HttpStatusCode != 0)
        {
            throw new ProbeRequestException((System.Net.HttpStatusCode)socket.HttpStatusCode,
                $"GeoGuessr refused the socket handshake with {(int)socket.HttpStatusCode}.");
        }

        var topic = $"club:{clubId:D}";
        emit($"Connected to {url.GetLeftPart(UriPartial.Path)}; subscribing to {topic}. Ctrl+C to stop.");
        await SendAsync(socket, $$"""{"code":"Subscribe","topic":"{{topic}}","client":"web"}""", listening.Token).ConfigureAwait(false);

        var heartbeat = HeartbeatAsync(socket, listening.Token);
        var payloads = new Dictionary<string, List<JsonElement>>(StringComparer.Ordinal);
        try
        {
            await foreach (var frame in ReadFramesAsync(socket, listening.Token).ConfigureAwait(false))
            {
                Report(frame, payloads, emit);
            }

            emit($"Closed by GeoGuessr: {(int?)socket.CloseStatus} {socket.CloseStatusDescription}");
        }
        catch (OperationCanceledException) when (listening.IsCancellationRequested)
        {
            emit("Stopped listening.");
        }
        catch (WebSocketException ex)
        {
            emit($"The connection failed: {ex.Message}");
        }
        finally
        {
            await listening.CancelAsync().ConfigureAwait(false);
            try
            {
                await heartbeat.ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or WebSocketException)
            {
                // Stopping.
            }
        }

        foreach (var (code, items) in payloads.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            emit(string.Empty);
            emit($"=== {code}: {items.Count} payload(s) ===");
            emit(FieldCensus.Build(items));
        }
    }

    private static void Report(string frame, Dictionary<string, List<JsonElement>> payloads, Action<string> emit)
    {
        string code;
        string? topic;
        JsonElement? payload = null;
        try
        {
            using var document = JsonDocument.Parse(frame);
            var root = document.RootElement;
            code = root.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString()! : "(no code)";
            topic = root.TryGetProperty("topic", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
            if (root.TryGetProperty("payload", out var p) && p.ValueKind == JsonValueKind.String && p.GetString() is { Length: > 0 } json)
            {
                try
                {
                    using var inner = JsonDocument.Parse(json);
                    payload = inner.RootElement.Clone();
                }
                catch (JsonException)
                {
                    // Reported raw below.
                }
            }
        }
        catch (JsonException)
        {
            emit($"[{DateTime.UtcNow:HH:mm:ss}] (not JSON) {frame}");
            return;
        }

        emit($"[{DateTime.UtcNow:HH:mm:ss}] {code} on {topic ?? "(no topic)"}");
        emit(TokenRedactor.Redact(frame));
        if (payload is { } item)
        {
            if (!payloads.TryGetValue(code, out var list))
            {
                payloads[code] = list = [];
            }

            list.Add(item);
        }
    }

    private static async Task HeartbeatAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(HeartbeatInterval);
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            await SendAsync(socket, """{"code":"HeartBeat"}""", cancellationToken).ConfigureAwait(false);
        }
    }

    private static Task SendAsync(ClientWebSocket socket, string frame, CancellationToken cancellationToken)
    {
        ReadOnlySocketGuard.EnsureAllowed(frame);
        return socket.SendAsync(Encoding.UTF8.GetBytes(frame), WebSocketMessageType.Text, true, cancellationToken);
    }

    private static async IAsyncEnumerable<string> ReadFramesAsync(
        ClientWebSocket socket, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var buffer = new byte[16 * 1024];
        using var message = new MemoryStream();
        while (socket.State == WebSocketState.Open)
        {
            var result = await socket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                yield break;
            }

            message.Write(buffer, 0, result.Count);
            if (!result.EndOfMessage)
            {
                continue;
            }

            if (result.MessageType == WebSocketMessageType.Text)
            {
                yield return Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
            }

            message.SetLength(0);
        }
    }
}
