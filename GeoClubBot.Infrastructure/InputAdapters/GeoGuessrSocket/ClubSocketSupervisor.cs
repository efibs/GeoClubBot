using System.Collections.Concurrent;
using System.Net.WebSockets;
using Configuration;
using Microsoft.Extensions.Logging;
using UseCases.Observability;
using UseCases.UseCases.ClubEvents;

namespace Infrastructure.InputAdapters.GeoGuessrSocket;

/// <summary>
/// Remembers which one-off reports (a field set, an unknown code, an unusable payload) were already
/// logged, across every club's socket, so each appears once per process.
/// </summary>
public sealed class SocketFrameReports
{
    private readonly ConcurrentDictionary<string, byte> _reported = new(StringComparer.Ordinal);

    public bool FirstTime(string key) => _reported.TryAdd(key, 0);
}

/// <summary>
/// Holds one club's socket: connects with the club's token, subscribes to its topic, asks for a
/// refresh of the club on every connection (whatever happened while it was down is unknown), sends the
/// heartbeat and passes the decoded events on. Reconnects as <see cref="SocketConnectionPolicy"/>
/// decides, and while the socket is down asks for a refresh every
/// <see cref="GeoGuessrClubEntry.GetSocketPollWhileDisconnected"/>.
/// </summary>
public sealed partial class ClubSocketSupervisor(
    GeoGuessrClubEntry club,
    IGeoGuessrSocketConnector connector,
    IClubEventSink sink,
    SocketFrameReports reports,
    GeoGuessrSocketConfiguration options,
    TimeProvider timeProvider,
    ILogger<ClubSocketSupervisor> logger)
{
    private const int MaxJitterMilliseconds = 250;

    private readonly SocketConnectionPolicy _policy = new(options);
    private readonly ClubConnectionAlerter _alerter = new(options, timeProvider);
    private readonly TimeSpan _pollInterval = club.GetSocketPollWhileDisconnected(options);
    private readonly Lock _coverage = new();
    private bool _connected;
    private DateTimeOffset _coveredAt;

    /// <summary>Runs until <paramref name="cancellationToken"/> is cancelled. Never throws anything else.</summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        // The start-up sync covers the club until the first poll is due.
        MarkCovered(connected: false);
        var polling = PollWhileDisconnectedAsync(cancellationToken);

        var state = SocketConnectionState.Initial;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    state = state.Phase == SocketPhase.Waiting
                        ? await WaitAsync(state, cancellationToken).ConfigureAwait(false)
                        : await ConnectAndReadAsync(state, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    LogUnexpectedFailure(ex, club.ClubId);
                    state = _policy.Next(state, new SocketSignal.ConnectFailed());
                }

                Report(state);
            }
        }
        finally
        {
            await IgnoreCancellationAsync(polling).ConfigureAwait(false);
        }
    }

    private async Task<SocketConnectionState> WaitAsync(SocketConnectionState state, CancellationToken cancellationToken)
    {
        await Task.Delay(state.Delay, timeProvider, cancellationToken).ConfigureAwait(false);
        return _policy.Next(state, new SocketSignal.WaitOver());
    }

    private async Task<SocketConnectionState> ConnectAndReadAsync(SocketConnectionState state, CancellationToken cancellationToken)
    {
        await Task.Delay(state.Delay + Jitter(), timeProvider, cancellationToken).ConfigureAwait(false);

        IGeoGuessrSocketSession session;
        try
        {
            session = await connector.ConnectAsync(club, state.IsReconnectAfterRestart, cancellationToken).ConfigureAwait(false);
        }
        catch (SocketHandshakeException ex)
        {
            LogHandshakeRefused(club.ClubId, ex.StatusCode);
            RecordConnection("refused", ex.StatusCode);
            return _policy.Next(state, new SocketSignal.HandshakeRefused(ex.StatusCode, ex.RetryAfter));
        }
        catch (Exception ex) when (ex is WebSocketException or HttpRequestException)
        {
            LogConnectFailed(club.ClubId, ex.Message);
            RecordConnection("failed", null);
            return _policy.Next(state, new SocketSignal.ConnectFailed());
        }

        var openedAt = timeProvider.GetUtcNow();
        bool denied;
        await using (session.ConfigureAwait(false))
        {
            state = _policy.Next(state, new SocketSignal.Opened());
            LogConnected(club.ClubId);
            RecordConnection("opened", null);
            Report(state);
            MarkCovered(connected: true);
            GeoGuessrSocketMetrics.LiveClubs.Add(1);
            try
            {
                denied = await ReadUntilClosedAsync(session, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                GeoGuessrSocketMetrics.LiveClubs.Add(-1);
                MarkCovered(connected: false);
            }
        }

        var connectedFor = timeProvider.GetUtcNow() - openedAt;
        if (denied)
        {
            RecordConnection("denied", null);
            return _policy.Next(state, new SocketSignal.SubscribeDenied(connectedFor));
        }

        LogClosed(club.ClubId, session.Closure?.Code, session.Closure?.Description, connectedFor);
        RecordConnection("closed", session.Closure?.Code);
        return _policy.Next(state, new SocketSignal.Closed(session.Closure?.Code, session.Closure?.Description, connectedFor));
    }

    /// <returns>Whether GeoGuessr refused the club's topic.</returns>
    private async Task<bool> ReadUntilClosedAsync(IGeoGuessrSocketSession session, CancellationToken cancellationToken)
    {
        using var connection = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var heartbeat = Task.CompletedTask;
        try
        {
            await session.SendAsync(OutboundFrames.Subscribe(SocketTopics.Club(club.ClubId)), connection.Token).ConfigureAwait(false);

            // Whatever happened while the socket was down is unknown: the club is read again.
            sink.Publish(new ClubRefreshRequested(club.ClubId, default, ClubRefreshReason.Connected));

            heartbeat = HeartbeatAsync(session, connection.Token);

            await foreach (var frame in session.ReadFramesAsync(connection.Token).ConfigureAwait(false))
            {
                if (Handle(frame))
                {
                    return true;
                }
            }

            return false;
        }
        catch (WebSocketException ex)
        {
            LogConnectionLost(club.ClubId, ex.Message);
            return false;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // A send timed out: the connection is as good as lost.
            LogConnectionLost(club.ClubId, "a send timed out");
            return false;
        }
        finally
        {
            await connection.CancelAsync().ConfigureAwait(false);
            await IgnoreCancellationAsync(heartbeat).ConfigureAwait(false);
        }
    }

    private async Task HeartbeatAsync(IGeoGuessrSocketSession session, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(options.HeartbeatInterval, timeProvider);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                await session.SendAsync(OutboundFrames.HeartBeat, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (WebSocketException)
        {
            // The read loop sees the same failure and reports it.
        }
    }

    /// <returns>Whether the frame was GeoGuessr refusing the club's topic.</returns>
    private bool Handle(string frame)
    {
        switch (ClubFrameDecoder.Decode(frame, club.ClubId))
        {
            case DecodedClubFrame.Push push:
                RecordFrame(push.Code, push.PayloadUnusable ? "fallback" : "event");
                if (push.FieldSet is { } fields && reports.FirstTime($"fields:{push.Code}:{fields}"))
                {
                    LogFieldSet(push.Code, fields);
                }

                if (push.PayloadUnusable)
                {
                    GeoGuessrSocketMetrics.PayloadFallbacks.Add(1, new KeyValuePair<string, object?>("code", push.Code));
                    if (reports.FirstTime($"fallback:{push.Code}"))
                    {
                        LogPayloadUnusable(push.Code, push.FieldSet ?? "no payload");
                    }
                }

                sink.Publish(push.Event);
                return false;

            case DecodedClubFrame.Subscribed:
                RecordFrame("Subscribed", "ack");
                LogSubscribed(club.ClubId);
                return false;

            case DecodedClubFrame.SubscribeDenied:
                RecordFrame("SubscribeDenied", "denied");
                return true;

            case DecodedClubFrame.Ignored ignored:
                RecordFrame(ignored.Code, "ignored");
                return false;

            case DecodedClubFrame.Unknown unknown:
                RecordFrame(unknown.Code, "unknown");
                if (reports.FirstTime($"unknown:{unknown.Code}"))
                {
                    LogUnknownCode(unknown.Code);
                }

                return false;

            default:
                RecordFrame("malformed", "malformed");
                if (reports.FirstTime("malformed"))
                {
                    LogMalformedFrame();
                }

                return false;
        }
    }

    /// <summary>
    /// While the club has no live socket, asks for a refresh whenever it went a whole interval
    /// without one. A connection counts as a refresh (it asks for one) and keeps the club covered.
    /// </summary>
    private async Task PollWhileDisconnectedAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            DateTimeOffset due;
            lock (_coverage)
            {
                due = _coveredAt + _pollInterval;
            }

            var wait = due - timeProvider.GetUtcNow();
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, timeProvider, cancellationToken).ConfigureAwait(false);
            }

            var refresh = false;
            lock (_coverage)
            {
                var now = timeProvider.GetUtcNow();
                if (_connected)
                {
                    _coveredAt = now;
                }
                else if (now - _coveredAt >= _pollInterval)
                {
                    _coveredAt = now;
                    refresh = true;
                }
            }

            if (refresh)
            {
                LogPollingWhileDisconnected(club.ClubId);
                sink.Publish(new ClubRefreshRequested(club.ClubId, default, ClubRefreshReason.Disconnected));
            }
        }
    }

    private void MarkCovered(bool connected)
    {
        lock (_coverage)
        {
            _connected = connected;
            _coveredAt = timeProvider.GetUtcNow();
        }
    }

    private void Report(SocketConnectionState state)
    {
        if (_alerter.Observe(state) is not { } alert)
        {
            return;
        }

        switch (alert.Kind)
        {
            case SocketAlertKind.Down:
                LogDown(club.ClubId, alert.Since, _pollInterval);
                break;
            case SocketAlertKind.Superseded:
                LogSuperseded(club.ClubId, state.Delay, _pollInterval);
                break;
            case SocketAlertKind.Blocked:
                LogBlocked(club.ClubId, state.Delay, _pollInterval);
                break;
            case SocketAlertKind.ClientOutdated:
                LogClientOutdated(club.ClubId, options.ClientVersion, state.Delay);
                break;
            case SocketAlertKind.AuthRejected:
                LogAuthRejected(club.ClubId, state.Delay);
                break;
            case SocketAlertKind.SubscribeDenied:
                LogSubscribeDenied(club.ClubId, state.Delay);
                break;
            case SocketAlertKind.Recovered:
                LogRecovered(club.ClubId, alert.Since);
                break;
        }
    }

    private static TimeSpan Jitter() => TimeSpan.FromMilliseconds(Random.Shared.Next(MaxJitterMilliseconds));

    private static void RecordFrame(string code, string outcome) =>
        GeoGuessrSocketMetrics.Frames.Add(1,
            new KeyValuePair<string, object?>("code", code),
            new KeyValuePair<string, object?>("outcome", outcome));

    private static void RecordConnection(string connectionEvent, int? code) =>
        GeoGuessrSocketMetrics.Connections.Add(1,
            new KeyValuePair<string, object?>("event", connectionEvent),
            new KeyValuePair<string, object?>("code", code));

    private static async Task IgnoreCancellationAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Stopping is the expected way out.
        }
    }

    [LoggerMessage(LogLevel.Information, "GeoGuessr socket of club {ClubId} connected.")]
    partial void LogConnected(Guid clubId);

    [LoggerMessage(LogLevel.Debug, "GeoGuessr acknowledged the topic of club {ClubId}.")]
    partial void LogSubscribed(Guid clubId);

    [LoggerMessage(LogLevel.Information, "GeoGuessr socket of club {ClubId} closed ({Code} {Description}) after {ConnectedFor}.")]
    partial void LogClosed(Guid clubId, int? code, string? description, TimeSpan connectedFor);

    [LoggerMessage(LogLevel.Information, "GeoGuessr socket of club {ClubId} lost: {Reason}.")]
    partial void LogConnectionLost(Guid clubId, string reason);

    [LoggerMessage(LogLevel.Information, "Connecting GeoGuessr's socket for club {ClubId} failed: {Reason}.")]
    partial void LogConnectFailed(Guid clubId, string reason);

    [LoggerMessage(LogLevel.Information, "GeoGuessr refused the socket handshake for club {ClubId} with status {Status}.")]
    partial void LogHandshakeRefused(Guid clubId, int status);

    [LoggerMessage(LogLevel.Error, "The GeoGuessr socket of club {ClubId} failed unexpectedly; reconnecting.")]
    partial void LogUnexpectedFailure(Exception ex, Guid clubId);

    [LoggerMessage(LogLevel.Information, "GeoGuessr socket frame {Code} carries the fields {Fields}.")]
    partial void LogFieldSet(string code, string fields);

    [LoggerMessage(LogLevel.Warning,
        "GeoGuessr's {Code} frame lacked what the bot needs (it carried: {Fields}); the roster is read again instead. " +
        "The payload format may have changed: see Documentation/GeoGuessrSocket.md.")]
    partial void LogPayloadUnusable(string code, string fields);

    [LoggerMessage(LogLevel.Information, "GeoGuessr's socket sent a frame with the unknown code {Code}.")]
    partial void LogUnknownCode(string code);

    [LoggerMessage(LogLevel.Information, "GeoGuessr's socket sent a frame that isn't the expected JSON.")]
    partial void LogMalformedFrame();

    [LoggerMessage(LogLevel.Debug, "Refreshing club {ClubId} from the API: its socket is down.")]
    partial void LogPollingWhileDisconnected(Guid clubId);

    [LoggerMessage(LogLevel.Warning,
        "GeoGuessr's socket for club {ClubId} has been down since {Since:u}; reconnecting. " +
        "The club is read from the API every {PollInterval} meanwhile.")]
    partial void LogDown(Guid clubId, DateTimeOffset since, TimeSpan pollInterval);

    [LoggerMessage(LogLevel.Warning,
        "GeoGuessr closed the socket for club {ClubId} as superseded (4100); reconnecting in {Delay}. " +
        "The club is read from the API every {PollInterval} meanwhile.")]
    partial void LogSuperseded(Guid clubId, TimeSpan delay, TimeSpan pollInterval);

    [LoggerMessage(LogLevel.Warning,
        "GeoGuessr refused the socket handshake for club {ClubId} with 403 (presumably Cloudflare); retrying in {Delay}. " +
        "The club is read from the API every {PollInterval} meanwhile.")]
    partial void LogBlocked(Guid clubId, TimeSpan delay, TimeSpan pollInterval);

    [LoggerMessage(LogLevel.Warning,
        "GeoGuessr asked the socket for club {ClubId} to reload (4200): GeoGuessrSocket:ClientVersion {ClientVersion} " +
        "is probably outdated. Retrying in {Delay}.")]
    partial void LogClientOutdated(Guid clubId, string clientVersion, TimeSpan delay);

    [LoggerMessage(LogLevel.Error,
        "GeoGuessr rejected the NcfaToken of club {ClubId} on its socket; replace the token. Retrying in {Delay}.")]
    partial void LogAuthRejected(Guid clubId, TimeSpan delay);

    [LoggerMessage(LogLevel.Error,
        "GeoGuessr refused the socket topic of club {ClubId}: its NcfaToken probably belongs to an account outside the club. " +
        "Retrying in {Delay}.")]
    partial void LogSubscribeDenied(Guid clubId, TimeSpan delay);

    [LoggerMessage(LogLevel.Information,
        "GeoGuessr's socket for club {ClubId} is back (down since {Since:u}); the club is read again for what was missed.")]
    partial void LogRecovered(Guid clubId, DateTimeOffset since);
}
