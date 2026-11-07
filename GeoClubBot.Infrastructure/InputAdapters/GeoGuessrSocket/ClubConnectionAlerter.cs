using Configuration;
using Microsoft.Extensions.Logging;

namespace Infrastructure.InputAdapters.GeoGuessrSocket;

/// <summary>What a socket state change is worth reporting.</summary>
public enum SocketAlertKind
{
    /// <summary>The socket has been down for <see cref="GeoGuessrSocketConfiguration.AlertAfter"/>.</summary>
    Down,

    Superseded,

    Blocked,

    ClientOutdated,

    AuthRejected,

    SubscribeDenied,

    /// <summary>The socket is back after one of the others was reported.</summary>
    Recovered,
}

/// <param name="Since">When the socket went down.</param>
public sealed record SocketAlert(SocketAlertKind Kind, LogLevel Level, DateTimeOffset Since);

/// <summary>
/// Decides which state changes of one club's socket are reported, so the log channel — which receives
/// every warning — hears about real problems and not about each brief reconnect. Ported from
/// GeoGuessrAccountShadowBot's announcer.
/// </summary>
public sealed class ClubConnectionAlerter(GeoGuessrSocketConfiguration options, TimeProvider timeProvider)
{
    private readonly Dictionary<SocketAlertKind, DateTimeOffset> _lastReported = [];
    private DateTimeOffset? _downSince;
    private bool _reportedThisOutage;
    private bool _explainedThisOutage;

    /// <summary>The report the new state calls for, if any.</summary>
    /// <remarks>
    /// Each kind is repeated at most once per its interval, also across outages, so a token that stays
    /// rejected is reported once a day however often the retry fails. Once a specific report has
    /// explained the outage, the generic "down" stays quiet: the socket passes through reconnecting on
    /// every retry, and the specific report repeats on its own interval.
    /// </remarks>
    public SocketAlert? Observe(SocketConnectionState state)
    {
        var now = timeProvider.GetUtcNow();

        if (state.Phase == SocketPhase.Connected)
        {
            var since = _downSince ?? now;
            var reported = _reportedThisOutage;
            _downSince = null;
            _reportedThisOutage = false;
            _explainedThisOutage = false;
            return reported ? new SocketAlert(SocketAlertKind.Recovered, LogLevel.Information, since) : null;
        }

        _downSince ??= now;

        return state switch
        {
            { Phase: SocketPhase.Waiting, Reason: SocketWaitReason.AuthRejected } =>
                Alert(SocketAlertKind.AuthRejected, LogLevel.Error, options.RepeatAuthAlertAfter, now),
            { Phase: SocketPhase.Waiting, Reason: SocketWaitReason.SubscribeDenied } =>
                Alert(SocketAlertKind.SubscribeDenied, LogLevel.Error, options.RepeatAuthAlertAfter, now),
            { Phase: SocketPhase.Waiting, Reason: SocketWaitReason.Blocked } =>
                Alert(SocketAlertKind.Blocked, LogLevel.Warning, options.RepeatAlertAfter, now),
            { Phase: SocketPhase.Waiting, Reason: SocketWaitReason.ClientOutdated } =>
                Alert(SocketAlertKind.ClientOutdated, LogLevel.Warning, options.RepeatAlertAfter, now),
            { Phase: SocketPhase.Waiting, Reason: SocketWaitReason.Superseded } =>
                Alert(SocketAlertKind.Superseded, LogLevel.Warning, options.RepeatAlertAfter, now),
            { Phase: SocketPhase.Connecting } when now - _downSince >= options.AlertAfter && !_explainedThisOutage =>
                Alert(SocketAlertKind.Down, LogLevel.Warning, options.RepeatAlertAfter, now),
            _ => null,
        };
    }

    private SocketAlert? Alert(SocketAlertKind kind, LogLevel level, TimeSpan repeatAfter, DateTimeOffset now)
    {
        _explainedThisOutage |= kind != SocketAlertKind.Down;
        if (_lastReported.TryGetValue(kind, out var last) && now - last < repeatAfter)
        {
            return null;
        }

        _lastReported[kind] = now;
        _reportedThisOutage = true;
        return new SocketAlert(kind, level, _downSince ?? now);
    }
}
