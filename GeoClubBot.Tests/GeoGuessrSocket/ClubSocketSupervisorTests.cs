using Configuration;
using FluentAssertions;
using Infrastructure.InputAdapters.GeoGuessrSocket;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using UseCases.UseCases.ClubEvents;
using Xunit;

namespace GeoClubBot.Tests.GeoGuessrSocket;

public sealed class ClubSocketSupervisorTests : IAsyncLifetime
{
    private static readonly Guid ClubId = Guid.Parse("d0c6b623-8df6-4654-860d-a4757cf78cf4");

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 7, 12, 0, 0, TimeSpan.Zero));
    private readonly ScriptedConnector _connector = new();
    private readonly RecordingSink _sink = new();
    private readonly CancellationTokenSource _stop = new();
    private Task? _run;

    private void Start(GeoGuessrSocketConfiguration? options = null, GeoGuessrClubEntry? club = null)
    {
        var supervisor = new ClubSocketSupervisor(
            club ?? new GeoGuessrClubEntry { ClubId = ClubId, NcfaToken = "x" },
            _connector, _sink, new SocketFrameReports(), options ?? new GeoGuessrSocketConfiguration(), _clock,
            NullLogger<ClubSocketSupervisor>.Instance);
        _run = supervisor.RunAsync(_stop.Token);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await _stop.CancelAsync();
        if (_run is not null)
        {
            await _run.WaitAsync(TimeSpan.FromSeconds(10));
        }

        _stop.Dispose();
    }

    private static string Frame(string code, string? payload = null) =>
        $$"""{"code":"{{code}}","topic":"club:{{ClubId}}","payload":{{(payload is null ? "null" : System.Text.Json.JsonSerializer.Serialize(payload))}},"accessToken":"t"}""";

    private int Refreshes(ClubRefreshReason reason) =>
        _sink.Events.OfType<ClubRefreshRequested>().Count(r => r.Reason == reason);

    [Fact]
    public async Task OnConnecting_SubscribesToTheClubsTopic_AndAsksForARefresh()
    {
        var session = _connector.Session();
        Start();

        await _clock.EventuallyAsync(() => Refreshes(ClubRefreshReason.Connected) == 1, TimeSpan.FromSeconds(5));

        session.Sent.Should().StartWith($$"""{"code":"Subscribe","topic":"club:{{ClubId}}","client":"web"}""");
        _sink.Events.Should().ContainSingle().Which.ClubId.Should().Be(ClubId);
    }

    [Fact]
    public async Task ReceivedEvents_ArePassedOn()
    {
        var session = _connector.Session();
        Start();
        await _clock.EventuallyAsync(() => Refreshes(ClubRefreshReason.Connected) == 1, TimeSpan.FromSeconds(5));

        session.Feed(Frame("ClubMemberLeft", """{"userId":"u1"}"""));
        session.Feed(Frame("ClubMemberCameOnline", """{"userId":"u1"}"""));
        session.Feed(Frame("ClubMissionBoardUpdated"));

        await _clock.EventuallyAsync(() => _sink.Events.Count == 3, TimeSpan.FromSeconds(5));
        _sink.Events.Skip(1).Select(e => e.GetType()).Should().Equal(typeof(ClubMemberLeftPush), typeof(ClubMissionBoardUpdatedPush));
    }

    [Fact]
    public async Task WhileConnected_SendsTheHeartbeat()
    {
        var session = _connector.Session();
        Start();
        await _clock.EventuallyAsync(() => session.Sent.Count == 1, TimeSpan.FromSeconds(5));

        await _clock.EventuallyAsync(() => session.Sent.Count >= 3, TimeSpan.FromSeconds(40));

        session.Sent.Skip(1).Should().AllBe(OutboundFrames.HeartBeat);
    }

    [Fact]
    public async Task AfterAServerRestart_ReconnectsAtOnce_SayingSo()
    {
        var first = _connector.Session();
        _connector.Session();
        Start();
        await _clock.EventuallyAsync(() => first.Sent.Count == 1, TimeSpan.FromSeconds(5));

        first.Close(1012);

        await _clock.EventuallyAsync(() => _connector.Attempts == 2, TimeSpan.FromSeconds(2));
        _connector.RestartFlags.Should().Equal(false, true);
        first.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task WhenSuperseded_YieldsBeforeReconnecting()
    {
        var first = _connector.Session();
        _connector.Session();
        Start();
        await _clock.EventuallyAsync(() => first.Sent.Count == 1, TimeSpan.FromSeconds(5));

        first.Close(4100, "superseded");
        await _clock.AdvanceSlowlyAsync(TimeSpan.FromMinutes(14), TimeSpan.FromSeconds(10));
        _connector.Attempts.Should().Be(1);

        await _clock.EventuallyAsync(() => _connector.Attempts == 2, TimeSpan.FromMinutes(2), TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task WhenTheTopicIsDenied_ClosesAndWaitsLikeForARejectedToken()
    {
        var session = _connector.Session();
        _connector.Session();
        Start();
        await _clock.EventuallyAsync(() => session.Sent.Count == 1, TimeSpan.FromSeconds(5));

        session.Feed(Frame("SubscribeDenied"));
        await _clock.EventuallyAsync(() => session.Disposed, TimeSpan.FromSeconds(5));

        await _clock.AdvanceSlowlyAsync(TimeSpan.FromMinutes(29), TimeSpan.FromSeconds(10));
        _connector.Attempts.Should().Be(1);
        await _clock.EventuallyAsync(() => _connector.Attempts == 2, TimeSpan.FromMinutes(2), TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task ARejectedToken_WaitsBeforeTheNextAttempt()
    {
        _connector.Throw(new SocketHandshakeException(401, null));
        Start();
        await _clock.EventuallyAsync(() => _connector.Attempts == 1, TimeSpan.FromSeconds(5));

        await _clock.AdvanceSlowlyAsync(TimeSpan.FromMinutes(29), TimeSpan.FromSeconds(10));
        _connector.Attempts.Should().Be(1);
        await _clock.EventuallyAsync(() => _connector.Attempts == 2, TimeSpan.FromMinutes(2), TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task WhileTheSocketIsDown_TheClubIsRefreshedEveryPollingInterval()
    {
        // Nothing scripted: every attempt fails.
        Start(new GeoGuessrSocketConfiguration { PollWhileDisconnected = TimeSpan.FromMinutes(10) });

        await _clock.AdvanceSlowlyAsync(TimeSpan.FromMinutes(9), TimeSpan.FromSeconds(5));
        Refreshes(ClubRefreshReason.Disconnected).Should().Be(0);

        await _clock.EventuallyAsync(() => Refreshes(ClubRefreshReason.Disconnected) == 2, TimeSpan.FromMinutes(12), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task AClubsOwnPollingInterval_Wins()
    {
        Start(club: new GeoGuessrClubEntry { ClubId = ClubId, NcfaToken = "x", SocketPollWhileDisconnected = TimeSpan.FromMinutes(5) });

        await _clock.EventuallyAsync(() => Refreshes(ClubRefreshReason.Disconnected) == 1, TimeSpan.FromMinutes(6), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task WhileConnected_TheClubIsNotPolled()
    {
        _connector.Session();
        Start(new GeoGuessrSocketConfiguration { PollWhileDisconnected = TimeSpan.FromMinutes(10) });
        await _clock.EventuallyAsync(() => Refreshes(ClubRefreshReason.Connected) == 1, TimeSpan.FromSeconds(5));

        await _clock.AdvanceSlowlyAsync(TimeSpan.FromMinutes(35), TimeSpan.FromSeconds(5));

        Refreshes(ClubRefreshReason.Disconnected).Should().Be(0);
    }

    [Fact]
    public async Task AnUnexpectedFailure_DoesNotStopTheSupervisor()
    {
        _connector.Throw(new InvalidOperationException("boom"));
        _connector.Session();
        Start();

        await _clock.EventuallyAsync(() => Refreshes(ClubRefreshReason.Connected) == 1, TimeSpan.FromSeconds(10));
        _run!.IsCompleted.Should().BeFalse();
    }

    [Fact]
    public async Task Stopping_EndsTheRun_AndClosesTheSocket()
    {
        var session = _connector.Session();
        Start();
        await _clock.EventuallyAsync(() => session.Sent.Count == 1, TimeSpan.FromSeconds(5));

        await _stop.CancelAsync();

        await _run!.WaitAsync(TimeSpan.FromSeconds(5));
        session.Disposed.Should().BeTrue();
    }
}
