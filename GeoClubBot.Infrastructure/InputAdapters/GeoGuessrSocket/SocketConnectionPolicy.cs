using Configuration;

namespace Infrastructure.InputAdapters.GeoGuessrSocket;

public enum SocketPhase
{
    /// <summary>Trying to open the socket, after <see cref="SocketConnectionState.Delay"/>.</summary>
    Connecting,

    Connected,

    /// <summary>Off the socket on purpose for <see cref="SocketConnectionState.Delay"/>; the club is polled meanwhile.</summary>
    Waiting,
}

/// <summary>Why the socket is <see cref="SocketPhase.Waiting"/>.</summary>
public enum SocketWaitReason
{
    None,

    /// <summary>GeoGuessr closed the socket with 4100.</summary>
    Superseded,

    /// <summary>The handshake was refused with 403, probably by Cloudflare.</summary>
    Blocked,

    /// <summary>GeoGuessr closed the socket with 4200: <see cref="GeoGuessrSocketConfiguration.ClientVersion"/> is probably stale.</summary>
    ClientOutdated,

    /// <summary>The club's token was rejected (401, or close 1001 "Unauthorized").</summary>
    AuthRejected,

    /// <summary>GeoGuessr refused the club topic: the token's account is probably not in the club.</summary>
    SubscribeDenied,
}

/// <param name="Delay">How long to wait before the next attempt.</param>
/// <param name="IsReconnectAfterRestart">GeoGuessr closed for a restart (1012), which the next connection reports.</param>
public sealed record SocketConnectionState(
    SocketPhase Phase,
    SocketWaitReason Reason = SocketWaitReason.None,
    int Attempt = 0,
    int YieldCount = 0,
    TimeSpan Delay = default,
    bool IsReconnectAfterRestart = false)
{
    public static SocketConnectionState Initial { get; } = new(SocketPhase.Connecting);
}

/// <summary>Something that happened to a club's socket.</summary>
public abstract record SocketSignal
{
    public sealed record Opened : SocketSignal;

    /// <summary>The socket ended. A null code means it failed without a closing handshake.</summary>
    public sealed record Closed(int? Code, string? Description, TimeSpan ConnectedFor) : SocketSignal;

    /// <summary>The server refused the handshake with an HTTP status.</summary>
    public sealed record HandshakeRefused(int Status, TimeSpan? RetryAfter) : SocketSignal;

    /// <summary>The handshake failed before the server answered.</summary>
    public sealed record ConnectFailed : SocketSignal;

    /// <summary>GeoGuessr refused the club topic; the bot closed the socket.</summary>
    public sealed record SubscribeDenied(TimeSpan ConnectedFor) : SocketSignal;

    /// <summary>The wait of <see cref="SocketPhase.Waiting"/> is over.</summary>
    public sealed record WaitOver : SocketSignal;
}

/// <summary>
/// Decides how a club's socket reacts to each signal. No side effects, so every close code is tested
/// without a socket. Ported from GeoGuessrAccountShadowBot, minus its HTTP token check: a rejected token
/// simply waits <see cref="GeoGuessrSocketConfiguration.AuthRetry"/>, while the club is polled.
/// </summary>
public sealed class SocketConnectionPolicy(GeoGuessrSocketConfiguration options)
{
    public const int CloseGoingAway = 1001;
    public const int CloseServerRestart = 1012;
    public const int CloseSuperseded = 4100;
    public const int CloseReload = 4200;

    private const int FirstApplicationCloseCode = 4000;

    private static readonly TimeSpan MinimumDelayAfterUnknownClose = TimeSpan.FromSeconds(30);

    public SocketConnectionState Next(SocketConnectionState state, SocketSignal signal) => signal switch
    {
        SocketSignal.Opened => state with
        {
            Phase = SocketPhase.Connected,
            Reason = SocketWaitReason.None,
            Delay = TimeSpan.Zero,
            IsReconnectAfterRestart = false,
        },
        SocketSignal.Closed closed => OnClosed(Stabilised(state, closed.ConnectedFor), closed),
        SocketSignal.HandshakeRefused refused => OnHandshakeRefused(state, refused),
        SocketSignal.ConnectFailed => Backoff(state),
        SocketSignal.SubscribeDenied denied => Wait(Stabilised(state, denied.ConnectedFor), SocketWaitReason.SubscribeDenied, options.AuthRetry),
        SocketSignal.WaitOver => state with
        {
            Phase = SocketPhase.Connecting,
            Reason = SocketWaitReason.None,
            Attempt = 0,
            Delay = TimeSpan.Zero,
        },
        _ => throw new ArgumentOutOfRangeException(nameof(signal), signal, null),
    };

    /// <summary>
    /// The wait before an attempt. It starts like the site's own client, then keeps growing, because a
    /// flat retry suits a tab somebody is looking at, not a bot running unattended.
    /// </summary>
    public TimeSpan BackoffDelay(int attempt) => attempt switch
    {
        <= 0 => TimeSpan.Zero,
        <= 2 => TimeSpan.FromSeconds(1),
        <= 8 => TimeSpan.FromSeconds(3),
        <= 16 => TimeSpan.FromSeconds(5),
        _ => Min(TimeSpan.FromSeconds(15 * Math.Pow(2, Math.Min(attempt - 17, 16))), options.MaxBackoff),
    };

    /// <summary>A connection that lasted proves the last one wasn't a storm, so the counters start over.</summary>
    private SocketConnectionState Stabilised(SocketConnectionState state, TimeSpan connectedFor) =>
        connectedFor >= options.StableAfter ? state with { Attempt = 0, YieldCount = 0 } : state;

    private SocketConnectionState OnClosed(SocketConnectionState state, SocketSignal.Closed closed)
    {
        switch (closed.Code)
        {
            case CloseServerRestart:
                return state with { Phase = SocketPhase.Connecting, Delay = TimeSpan.Zero, IsReconnectAfterRestart = true };

            case CloseSuperseded:
                return Wait(
                    state with { YieldCount = state.YieldCount + 1 },
                    SocketWaitReason.Superseded,
                    Doubled(options.SupersededYieldInitial, state.YieldCount, options.SupersededYieldMax));

            case CloseReload:
                return Wait(state, SocketWaitReason.ClientOutdated, options.ClientOutdatedRetry);

            case CloseGoingAway when closed.Description == "Unauthorized":
                return Wait(state, SocketWaitReason.AuthRejected, options.AuthRetry);

            case >= FirstApplicationCloseCode:
                var next = Backoff(state);
                return next with { Delay = Max(next.Delay, MinimumDelayAfterUnknownClose) };

            default:
                return Backoff(state);
        }
    }

    private SocketConnectionState OnHandshakeRefused(SocketConnectionState state, SocketSignal.HandshakeRefused refused)
    {
        switch (refused.Status)
        {
            case 401:
                return Wait(state, SocketWaitReason.AuthRejected, options.AuthRetry);

            case 403:
                return Wait(
                    state with { YieldCount = state.YieldCount + 1 },
                    SocketWaitReason.Blocked,
                    Doubled(options.BlockedRetry, state.YieldCount, options.BlockedRetryMax));

            case 429:
                var next = Backoff(state);
                return next with { Delay = Max(next.Delay, refused.RetryAfter ?? TimeSpan.Zero) };

            default:
                return Backoff(state);
        }
    }

    private SocketConnectionState Backoff(SocketConnectionState state)
    {
        var attempt = state.Attempt + 1;
        return state with
        {
            Phase = SocketPhase.Connecting,
            Reason = SocketWaitReason.None,
            Attempt = attempt,
            Delay = BackoffDelay(attempt),
        };
    }

    private static SocketConnectionState Wait(SocketConnectionState state, SocketWaitReason reason, TimeSpan delay) =>
        state with { Phase = SocketPhase.Waiting, Reason = reason, Delay = delay, IsReconnectAfterRestart = false };

    private static TimeSpan Doubled(TimeSpan initial, int times, TimeSpan max) =>
        Min(initial * Math.Pow(2, Math.Min(times, 16)), max);

    private static TimeSpan Min(TimeSpan first, TimeSpan second) => first < second ? first : second;

    private static TimeSpan Max(TimeSpan first, TimeSpan second) => first > second ? first : second;
}
