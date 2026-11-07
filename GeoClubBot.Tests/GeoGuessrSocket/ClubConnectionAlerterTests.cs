using Configuration;
using FluentAssertions;
using Infrastructure.InputAdapters.GeoGuessrSocket;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GeoClubBot.Tests.GeoGuessrSocket;

public sealed class ClubConnectionAlerterTests
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 7, 12, 0, 0, TimeSpan.Zero));
    private readonly ClubConnectionAlerter _alerter;

    public ClubConnectionAlerterTests() => _alerter = new ClubConnectionAlerter(new GeoGuessrSocketConfiguration(), _clock);

    private static readonly SocketConnectionState Connected = new(SocketPhase.Connected);
    private static readonly SocketConnectionState Reconnecting = new(SocketPhase.Connecting, Attempt: 1);

    private static SocketConnectionState Waiting(SocketWaitReason reason) => new(SocketPhase.Waiting, reason);

    [Fact]
    public void ABriefDrop_IsNotReported()
    {
        _alerter.Observe(Reconnecting).Should().BeNull();
        _clock.Advance(TimeSpan.FromSeconds(90));
        _alerter.Observe(Reconnecting).Should().BeNull();
        _alerter.Observe(Connected).Should().BeNull();
    }

    [Fact]
    public void ALongerOutage_IsReportedOnce_ThenAgainAfterTheRepeatInterval_AndItsEndToo()
    {
        var since = _clock.GetUtcNow();
        _alerter.Observe(Reconnecting);
        _clock.Advance(TimeSpan.FromMinutes(2));

        _alerter.Observe(Reconnecting).Should().Be(new SocketAlert(SocketAlertKind.Down, LogLevel.Warning, since));
        _clock.Advance(TimeSpan.FromHours(1));
        _alerter.Observe(Reconnecting).Should().BeNull();
        _clock.Advance(TimeSpan.FromHours(5));
        _alerter.Observe(Reconnecting)!.Kind.Should().Be(SocketAlertKind.Down);

        _alerter.Observe(Connected).Should().Be(new SocketAlert(SocketAlertKind.Recovered, LogLevel.Information, since));
        _alerter.Observe(Connected).Should().BeNull();
    }

    [Theory]
    [InlineData(SocketWaitReason.AuthRejected, SocketAlertKind.AuthRejected, LogLevel.Error)]
    [InlineData(SocketWaitReason.SubscribeDenied, SocketAlertKind.SubscribeDenied, LogLevel.Error)]
    [InlineData(SocketWaitReason.Blocked, SocketAlertKind.Blocked, LogLevel.Warning)]
    [InlineData(SocketWaitReason.ClientOutdated, SocketAlertKind.ClientOutdated, LogLevel.Warning)]
    [InlineData(SocketWaitReason.Superseded, SocketAlertKind.Superseded, LogLevel.Warning)]
    public void AWaitForAReason_IsReportedAtOnce(SocketWaitReason reason, SocketAlertKind kind, LogLevel level)
    {
        var alert = _alerter.Observe(Waiting(reason));

        alert.Should().NotBeNull();
        alert!.Kind.Should().Be(kind);
        alert.Level.Should().Be(level);
    }

    [Fact]
    public void ARejectedToken_IsRepeatedOnlyOnceADay()
    {
        _alerter.Observe(Waiting(SocketWaitReason.AuthRejected));
        _clock.Advance(TimeSpan.FromHours(12));
        _alerter.Observe(Reconnecting);
        _alerter.Observe(Waiting(SocketWaitReason.AuthRejected)).Should().BeNull();

        _clock.Advance(TimeSpan.FromHours(12));
        _alerter.Observe(Waiting(SocketWaitReason.AuthRejected)).Should().NotBeNull();
    }

    [Fact]
    public void RetryingARejectedToken_ReportsNothingMore_NotEvenTheOutage()
    {
        _alerter.Observe(Waiting(SocketWaitReason.AuthRejected)).Should().NotBeNull();

        // Every 30 minutes: the wait ends, the socket reconnects and is refused again.
        for (var i = 0; i < 20; i++)
        {
            _clock.Advance(TimeSpan.FromMinutes(30));
            _alerter.Observe(Reconnecting).Should().BeNull();
            _alerter.Observe(Waiting(SocketWaitReason.AuthRejected)).Should().BeNull();
        }
    }

    [Fact]
    public void TheEndOfAnOutage_IsReportedOnlyIfItsStartWas()
    {
        _alerter.Observe(Reconnecting);
        _alerter.Observe(Connected).Should().BeNull();

        _alerter.Observe(Waiting(SocketWaitReason.ClientOutdated));
        _alerter.Observe(Connected)!.Kind.Should().Be(SocketAlertKind.Recovered);
    }
}
