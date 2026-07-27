using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GeoClubBot.Discord.Services;

/// <summary>
/// Answers interactions for the "Launch" Entry Point Command that Discord.Net cannot see.
///
/// Discord.Net's <c>ApplicationCommandType</c> enum predates entry point commands, so when Discord
/// dispatches the interaction over Discord.Net's own gateway connection, <c>SocketInteraction.Create</c>
/// silently returns null — no event, no crash, just nothing usable. Reaching that interaction's raw
/// payload from outside would otherwise require either reimplementing Discord.Net's internal
/// zlib-stream decompression or reflecting into its private internals, both of which would be
/// coupled to undocumented implementation details that can change on any Discord.Net version bump.
///
/// Instead, this is a small, independent connection to Discord's public, documented Gateway API
/// (deliberately separate from Discord.Net's own connection and its intents/session), whose only job
/// is to catch that one interaction and immediately answer it with a <c>LAUNCH_ACTIVITY</c> (12)
/// callback — which launches the Activity without posting any channel message. Interactions aren't
/// gated by gateway intents, so this identifies with none. It does not support the RESUME opcode: any
/// disconnect reconnects from scratch after a short delay, which is an acceptable trade-off for a
/// non-critical, self-healing feature.
/// </summary>
public sealed class EntryPointCommandGatewayListener(
    HttpClient httpClient,
    IOptions<DiscordConfiguration> config,
    ILogger<EntryPointCommandGatewayListener> logger) : BackgroundService
{
    private const int EntryPointCommandType = 4;
    private const int ApplicationCommandInteractionType = 2;
    private const int LaunchActivityCallbackType = 12;
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(5);

    private int _lastSequence = -1;
    private int _awaitingHeartbeatAck;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Entry point activity gateway listener failed; reconnecting.");
            }

            try
            {
                await Task.Delay(ReconnectDelay, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task RunOnceAsync(CancellationToken stoppingToken)
    {
        _lastSequence = -1;
        _awaitingHeartbeatAck = 0;

        var gatewayUrl = await GetGatewayUrlAsync(stoppingToken).ConfigureAwait(false);

        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(new Uri($"{gatewayUrl}?v=10&encoding=json"), stoppingToken).ConfigureAwait(false);

        using var connectionCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        Task? heartbeatTask = null;

        try
        {
            while (socket.State == WebSocketState.Open)
            {
                var message = await ReceiveMessageAsync(socket, connectionCts.Token).ConfigureAwait(false);
                if (message is null)
                {
                    return;
                }

                using var doc = JsonDocument.Parse(message);
                var root = doc.RootElement;

                if (root.TryGetProperty("s", out var seqElement) && seqElement.ValueKind == JsonValueKind.Number)
                {
                    _lastSequence = seqElement.GetInt32();
                }

                switch (root.GetProperty("op").GetInt32())
                {
                    case 10: // Hello
                        var heartbeatIntervalMs = root.GetProperty("d").GetProperty("heartbeat_interval").GetInt32();
                        await SendIdentifyAsync(socket, connectionCts.Token).ConfigureAwait(false);
                        heartbeatTask = Task.Run(
                            () => HeartbeatLoopAsync(socket, heartbeatIntervalMs, connectionCts),
                            connectionCts.Token);
                        break;

                    case 1: // Heartbeat requested by Discord
                        await SendHeartbeatAsync(socket, connectionCts.Token).ConfigureAwait(false);
                        break;

                    case 11: // Heartbeat ACK
                        Interlocked.Exchange(ref _awaitingHeartbeatAck, 0);
                        break;

                    case 7: // Reconnect
                    case 9: // Invalid session
                        return;

                    case 0: // Dispatch
                        if (root.TryGetProperty("t", out var tElement) && tElement.ValueEquals("INTERACTION_CREATE"))
                        {
                            await HandleInteractionCreateAsync(root.GetProperty("d"), stoppingToken).ConfigureAwait(false);
                        }
                        break;
                }
            }
        }
        finally
        {
            await connectionCts.CancelAsync().ConfigureAwait(false);
            if (heartbeatTask is not null)
            {
                await heartbeatTask.WaitAsync(CancellationToken.None).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            }

            if (socket.State == WebSocketState.Open)
            {
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "reconnecting", CancellationToken.None)
                    .ConfigureAwait(false);
            }
        }
    }

    private async Task<string> GetGatewayUrlAsync(CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync("gateway", cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<GatewayResponse>(cancellationToken).ConfigureAwait(false);
        return payload?.Url ?? throw new InvalidOperationException("Discord did not return a gateway URL.");
    }

    private Task SendIdentifyAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        var identify = new
        {
            op = 2,
            d = new
            {
                token = config.Value.BotToken,
                intents = 0, // Interactions aren't gated by intents.
                properties = new { os = "linux", browser = "GeoClubBot", device = "GeoClubBot" }
            }
        };

        return SendAsync(socket, identify, cancellationToken);
    }

    private async Task HeartbeatLoopAsync(ClientWebSocket socket, int intervalMs, CancellationTokenSource connectionCts)
    {
        var cancellationToken = connectionCts.Token;
        try
        {
            // Discord docs: jitter the first heartbeat, then send on a steady interval.
            await Task.Delay(TimeSpan.FromMilliseconds(intervalMs * Random.Shared.NextDouble()), cancellationToken)
                .ConfigureAwait(false);

            while (!cancellationToken.IsCancellationRequested)
            {
                if (Interlocked.Exchange(ref _awaitingHeartbeatAck, 1) == 1)
                {
                    // Missed the previous ACK: the connection is a zombie. Force a reconnect.
                    await connectionCts.CancelAsync().ConfigureAwait(false);
                    return;
                }

                await SendHeartbeatAsync(socket, cancellationToken).ConfigureAwait(false);
                await Task.Delay(intervalMs, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown/reconnect.
        }
    }

    private Task SendHeartbeatAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        var payload = new { op = 1, d = _lastSequence < 0 ? (int?)null : _lastSequence };
        return SendAsync(socket, payload, cancellationToken);
    }

    private static Task SendAsync(ClientWebSocket socket, object payload, CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        return socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, cancellationToken);
    }

    private async Task<string?> ReceiveMessageAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        using var stream = new MemoryStream();

        while (true)
        {
            WebSocketReceiveResult result;
            try
            {
                result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken).ConfigureAwait(false);
            }
            catch (WebSocketException ex)
            {
                // Expected, routine occurrence: this connection deliberately doesn't support RESUME
                // (see class remarks), so every disconnect — including ordinary ones where Discord
                // drops the socket without completing the close handshake — ends up here. Logging
                // this at Warning spammed the Discord channel sink with non-actionable noise on every
                // reconnect; Information keeps it visible in regular logs without paging anyone.
                logger.LogInformation(ex, "Entry point activity gateway socket receive failed; reconnecting.");
                return null;
            }

            if (result.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }

            stream.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
            {
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }
    }

    private async Task HandleInteractionCreateAsync(JsonElement interaction, CancellationToken cancellationToken)
    {
        if (interaction.GetProperty("type").GetInt32() != ApplicationCommandInteractionType)
        {
            return;
        }

        if (!interaction.TryGetProperty("data", out var data) ||
            !data.TryGetProperty("type", out var typeElement) ||
            typeElement.GetInt32() != EntryPointCommandType)
        {
            return;
        }

        var id = interaction.GetProperty("id").GetString();
        var token = interaction.GetProperty("token").GetString();
        if (id is null || token is null)
        {
            return;
        }

        try
        {
            using var response = await httpClient
                .PostAsJsonAsync($"interactions/{id}/{token}/callback", new { type = LaunchActivityCallbackType }, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Failed to acknowledge an entry point Launch interaction ({StatusCode}).", response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to acknowledge an entry point Launch interaction.");
        }
    }

    private sealed record GatewayResponse([property: JsonPropertyName("url")] string? Url);
}
