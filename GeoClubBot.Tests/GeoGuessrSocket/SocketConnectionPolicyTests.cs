using Configuration;
using FluentAssertions;
using Infrastructure.InputAdapters.GeoGuessrSocket;
using Xunit;

namespace GeoClubBot.Tests.GeoGuessrSocket;

public sealed class SocketConnectionPolicyTests
{
    private static readonly GeoGuessrSocketConfiguration Options = new();
    private readonly SocketConnectionPolicy _policy = new(Options);

    private static readonly SocketConnectionState Connected = new(SocketPhase.Connected);

    private static SocketSignal.Closed Closed(int? code, string? description = null, double connectedSeconds = 5) =>
        new(code, description, TimeSpan.FromSeconds(connectedSeconds));

    [Fact]
    public void Opening_IsConnected_AndForgetsTheRestartFlag()
    {
        var next = _policy.Next(new SocketConnectionState(SocketPhase.Connecting, IsReconnectAfterRestart: true, Delay: TimeSpan.FromSeconds(3)), new SocketSignal.Opened());

        next.Should().Be(new SocketConnectionState(SocketPhase.Connected));
    }

    [Fact]
    public void AServerRestart_ReconnectsAtOnce_SayingSo()
    {
        var next = _policy.Next(Connected, Closed(1012));

        next.Phase.Should().Be(SocketPhase.Connecting);
        next.Delay.Should().Be(TimeSpan.Zero);
        next.IsReconnectAfterRestart.Should().BeTrue();
    }

    [Fact]
    public void BeingSuperseded_Yields_DoublingUpToTheMaximum()
    {
        var first = _policy.Next(Connected, Closed(4100));
        first.Should().Match<SocketConnectionState>(s => s.Phase == SocketPhase.Waiting && s.Reason == SocketWaitReason.Superseded);
        first.Delay.Should().Be(TimeSpan.FromMinutes(15));

        var state = first;
        var delays = new List<TimeSpan>();
        for (var i = 0; i < 4; i++)
        {
            state = _policy.Next(_policy.Next(_policy.Next(state, new SocketSignal.WaitOver()), new SocketSignal.Opened()), Closed(4100));
            delays.Add(state.Delay);
        }

        delays.Should().Equal(TimeSpan.FromMinutes(30), TimeSpan.FromHours(1), TimeSpan.FromHours(1), TimeSpan.FromHours(1));
    }

    [Fact]
    public void AStableConnection_StartsTheYieldOver()
    {
        var yielded = _policy.Next(Connected, Closed(4100));
        var reconnected = _policy.Next(_policy.Next(yielded, new SocketSignal.WaitOver()), new SocketSignal.Opened());

        var next = _policy.Next(reconnected, Closed(4100, connectedSeconds: 3600));

        next.Delay.Should().Be(TimeSpan.FromMinutes(15));
    }

    [Fact]
    public void AnOutdatedClient_Waits()
    {
        var next = _policy.Next(Connected, Closed(4200));

        next.Should().Match<SocketConnectionState>(s => s.Phase == SocketPhase.Waiting && s.Reason == SocketWaitReason.ClientOutdated);
        next.Delay.Should().Be(Options.ClientOutdatedRetry);
    }

    [Fact]
    public void ARejectedToken_Waits_WhetherClosedOrRefused()
    {
        foreach (var next in new[]
                 {
                     _policy.Next(Connected, Closed(1001, "Unauthorized")),
                     _policy.Next(SocketConnectionState.Initial, new SocketSignal.HandshakeRefused(401, null)),
                 })
        {
            next.Should().Match<SocketConnectionState>(s => s.Phase == SocketPhase.Waiting && s.Reason == SocketWaitReason.AuthRejected);
            next.Delay.Should().Be(Options.AuthRetry);
        }
    }

    [Fact]
    public void ADeniedTopic_WaitsLikeARejectedToken()
    {
        var next = _policy.Next(Connected, new SocketSignal.SubscribeDenied(TimeSpan.FromSeconds(1)));

        next.Should().Match<SocketConnectionState>(s => s.Phase == SocketPhase.Waiting && s.Reason == SocketWaitReason.SubscribeDenied);
        next.Delay.Should().Be(Options.AuthRetry);
    }

    [Fact]
    public void GoingAwayForAnotherReason_IsAnOrdinaryReconnect()
    {
        _policy.Next(Connected, Closed(1001, "bye")).Phase.Should().Be(SocketPhase.Connecting);
    }

    [Fact]
    public void ABlockedHandshake_Waits_DoublingUpToTheMaximum()
    {
        var first = _policy.Next(SocketConnectionState.Initial, new SocketSignal.HandshakeRefused(403, null));
        first.Should().Match<SocketConnectionState>(s => s.Phase == SocketPhase.Waiting && s.Reason == SocketWaitReason.Blocked);
        first.Delay.Should().Be(TimeSpan.FromHours(1));

        var state = first;
        for (var i = 0; i < 4; i++)
        {
            state = _policy.Next(_policy.Next(state, new SocketSignal.WaitOver()), new SocketSignal.HandshakeRefused(403, null));
        }

        state.Delay.Should().Be(TimeSpan.FromHours(6));
    }

    [Fact]
    public void TooManyRequests_WaitsAtLeastRetryAfter()
    {
        var next = _policy.Next(SocketConnectionState.Initial, new SocketSignal.HandshakeRefused(429, TimeSpan.FromMinutes(2)));

        next.Phase.Should().Be(SocketPhase.Connecting);
        next.Delay.Should().Be(TimeSpan.FromMinutes(2));
    }

    [Fact]
    public void AnUnknownApplicationClose_WaitsAtLeastHalfAMinute()
    {
        _policy.Next(Connected, Closed(4321)).Delay.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void Failures_BackOffLikeTheSite_ThenGrowToTheMaximum()
    {
        var delays = new List<TimeSpan>();
        var state = SocketConnectionState.Initial;
        for (var i = 0; i < 24; i++)
        {
            state = _policy.Next(state, new SocketSignal.ConnectFailed());
            delays.Add(state.Delay);
        }

        delays.Take(2).Should().AllBeEquivalentTo(TimeSpan.FromSeconds(1));
        delays.Skip(2).Take(6).Should().AllBeEquivalentTo(TimeSpan.FromSeconds(3));
        delays.Skip(8).Take(8).Should().AllBeEquivalentTo(TimeSpan.FromSeconds(5));
        delays[16].Should().Be(TimeSpan.FromSeconds(15));
        delays[17].Should().Be(TimeSpan.FromSeconds(30));
        delays[^1].Should().Be(Options.MaxBackoff);
    }

    [Fact]
    public void AStableConnection_StartsTheBackoffOver()
    {
        var state = SocketConnectionState.Initial;
        for (var i = 0; i < 10; i++)
        {
            state = _policy.Next(state, new SocketSignal.ConnectFailed());
        }

        var next = _policy.Next(_policy.Next(state, new SocketSignal.Opened()), Closed(null, connectedSeconds: 120));

        next.Attempt.Should().Be(1);
        next.Delay.Should().Be(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void AnEndedWait_ConnectsAtOnce()
    {
        var waiting = _policy.Next(Connected, Closed(4200));

        _policy.Next(waiting, new SocketSignal.WaitOver()).Should().Be(waiting with
        {
            Phase = SocketPhase.Connecting,
            Reason = SocketWaitReason.None,
            Attempt = 0,
            Delay = TimeSpan.Zero,
        });
    }
}
