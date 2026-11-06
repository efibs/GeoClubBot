using System.Buffers;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using Configuration;
using Microsoft.Extensions.Options;

namespace Infrastructure.InputAdapters.GeoGuessrSocket;

/// <summary>
/// Connects the way the site's own client does: a new tab id per connection and the session in the
/// <c>_ncfa</c> cookie. A browser and several such connections coexist on one account, so nothing is
/// done to take a connection over or keep it exclusive.
/// </summary>
/// <remarks>Ported from GeoGuessrAccountShadowBot, where the protocol was first observed.</remarks>
public sealed class ClientWebSocketConnector(IOptions<GeoGuessrSocketConfiguration> options) : IGeoGuessrSocketConnector
{
    private const int FirstAttempt = 1;

    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(30);

    public async Task<IGeoGuessrSocketSession> ConnectAsync(
        GeoGuessrClubEntry club, bool afterServerRestart, CancellationToken cancellationToken)
    {
        var settings = options.Value;

        var socket = new ClientWebSocket();
        socket.Options.CollectHttpResponseDetails = true;
        // The token is the cookie value as the browser holds it, exactly as the HTTP clients send it.
        socket.Options.SetRequestHeader("Cookie", $"_ncfa={club.NcfaToken}");
        socket.Options.SetRequestHeader("Origin", settings.Origin);
        socket.Options.SetRequestHeader("User-Agent", settings.UserAgent);
        socket.Options.KeepAliveInterval = settings.KeepAlive;
        socket.Options.KeepAliveTimeout = settings.KeepAlive;

        var url = new UriBuilder(settings.Url) { Query = Query(settings.ClientVersion, afterServerRestart) }.Uri;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ConnectTimeout);
        try
        {
            await socket.ConnectAsync(url, timeout.Token).ConfigureAwait(false);
        }
        catch (WebSocketException exception) when (socket.HttpStatusCode != 0)
        {
            var status = (int)socket.HttpStatusCode;
            var retryAfter = ReadRetryAfter(socket);
            socket.Dispose();
            throw new SocketHandshakeException(status, retryAfter, exception);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            socket.Dispose();
            throw new WebSocketException(WebSocketError.Faulted, "Connecting to GeoGuessr's socket timed out.", exception);
        }
        catch
        {
            socket.Dispose();
            throw;
        }

        // Response headers aren't needed any more, and may hold a Set-Cookie.
        socket.HttpResponseHeaders = null;
        return new ClientWebSocketSession(socket, settings.MaxFrameBytes);
    }

    private static string Query(string clientVersion, bool afterServerRestart)
    {
        var query = $"c={Uri.EscapeDataString(clientVersion)}&tabId={Guid.NewGuid()}&attempt={FirstAttempt}&visibility=visible";
        return afterServerRestart ? query + "&reconnect=true" : query;
    }

    private static TimeSpan? ReadRetryAfter(ClientWebSocket socket) =>
        socket.HttpResponseHeaders is { } headers
        && headers.TryGetValue("Retry-After", out var values)
        && int.TryParse(values.FirstOrDefault(), out var seconds)
            ? TimeSpan.FromSeconds(seconds)
            : null;
}

/// <summary>
/// One open <see cref="ClientWebSocket"/>. It allows one send at a time, so a lock serialises the
/// subscription and the heartbeats.
/// </summary>
public sealed class ClientWebSocketSession(ClientWebSocket socket, int maxFrameBytes) : IGeoGuessrSocketSession
{
    private const int ReceiveBufferBytes = 16 * 1024;

    /// <summary>What the site sends when it closes a socket on purpose.</summary>
    private const WebSocketCloseStatus OwnCloseCode = (WebSocketCloseStatus)4000;

    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(5);

    private readonly SemaphoreSlim _sendLock = new(1, 1);

    public SocketClosure? Closure { get; private set; }

    public async Task SendAsync(string frame, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(SendTimeout);

        await _sendLock.WaitAsync(timeout.Token).ConfigureAwait(false);
        try
        {
            await socket.SendAsync(Encoding.UTF8.GetBytes(frame), WebSocketMessageType.Text, true, timeout.Token).ConfigureAwait(false);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public async IAsyncEnumerable<string> ReadFramesAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(ReceiveBufferBytes);
        var message = new ArrayBufferWriter<byte>(ReceiveBufferBytes);

        try
        {
            while (socket.State == WebSocketState.Open)
            {
                var result = await ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (result is null)
                {
                    yield break;
                }

                if (result.Value.MessageType == WebSocketMessageType.Close)
                {
                    Closure = new SocketClosure((int?)socket.CloseStatus, socket.CloseStatusDescription);
                    yield break;
                }

                message.Write(buffer.AsSpan(0, result.Value.Count));
                if (message.WrittenCount > maxFrameBytes)
                {
                    await CloseOutputAsync(WebSocketCloseStatus.MessageTooBig).ConfigureAwait(false);
                    Closure = new SocketClosure(null, "frame too big");
                    yield break;
                }

                if (!result.Value.EndOfMessage)
                {
                    continue;
                }

                // Binary frames belong to game server sockets; here they mean nothing.
                if (result.Value.MessageType == WebSocketMessageType.Text)
                {
                    yield return Encoding.UTF8.GetString(message.WrittenSpan);
                }

                message.ResetWrittenCount();
            }

            Closure ??= new SocketClosure((int?)socket.CloseStatus, socket.CloseStatusDescription);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await CloseOutputAsync(OwnCloseCode).ConfigureAwait(false);
        socket.Dispose();
        _sendLock.Dispose();
    }

    private async Task<ValueWebSocketReceiveResult?> ReceiveAsync(byte[] buffer, CancellationToken cancellationToken)
    {
        try
        {
            return await socket.ReceiveAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
        }
        catch (WebSocketException)
        {
            Closure = new SocketClosure(null, "transport failure");
            return null;
        }
    }

    private async Task CloseOutputAsync(WebSocketCloseStatus status)
    {
        if (socket.State is not (WebSocketState.Open or WebSocketState.CloseReceived))
        {
            return;
        }

        using var timeout = new CancellationTokenSource(CloseTimeout);
        try
        {
            await socket.CloseOutputAsync(status, null, timeout.Token).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is WebSocketException or OperationCanceledException)
        {
            // Best effort: the connection is being abandoned either way.
        }
    }
}
