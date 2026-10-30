using Configuration;
using Entities;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.OutputPorts.Repositories;
using UseCases.UseCases.MissionBoard;
using Xunit;
using static GeoClubBot.Tests.TestBuilders.MissionBoards;

namespace GeoClubBot.Tests.Application.UseCases.MissionBoard;

/// <summary>
/// Unit tests for <see cref="RecordMissionClaimsHandler"/>, which copies the board's claims into
/// the database because GeoGuessr forgets them after two weeks (issue #402).
/// </summary>
public sealed class RecordMissionClaimsHandlerTests
{
    private readonly IClubMissionBoardReader _boardReader = Substitute.For<IClubMissionBoardReader>();
    private readonly IMissionClaimRepository _claims = Substitute.For<IMissionClaimRepository>();
    private readonly Guid _clubA = Guid.NewGuid();
    private readonly Guid _clubB = Guid.NewGuid();
    private readonly List<MissionClaim> _added = [];

    public RecordMissionClaimsHandlerTests()
    {
        _claims.ReadForUpdateByPeriodAsync(Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns([]);
        _claims.When(c => c.Add(Arg.Any<MissionClaim>())).Do(call => _added.Add(call.Arg<MissionClaim>()));
    }

    private RecordMissionClaimsHandler CreateHandler(bool enabled = true) =>
        new(
            _boardReader,
            _claims,
            Options.Create(new MissionClaimTrackingConfiguration { Schedule = "0 0 * ? * * *", Enabled = enabled, Retention = TimeSpan.FromDays(30) }),
            Options.Create(new GeoGuessrConfiguration
            {
                SyncSchedule = "0 0 0 * * ?",
                ActivityNcfaToken = "x",
                UserProfileNcfaToken = "x",
                Clubs =
                [
                    new GeoGuessrClubEntry { ClubId = _clubA, NcfaToken = "x", IsMain = true },
                    new GeoGuessrClubEntry { ClubId = _clubB, NcfaToken = "x" }
                ],
            }),
            NullLogger<RecordMissionClaimsHandler>.Instance);

    [Fact]
    public async Task Handle_AddsEveryClaimedMission_AndSkipsFreeOnes()
    {
        var claimedAt = PeriodStart.AddHours(2);
        var week = Week(Board(1,
            Tile(claimedBy: "alice", claimedAt: claimedAt, completed: true),
            Tile(index: 1, claimedBy: "bob", claimedAt: claimedAt),
            Tile(index: 2)));
        _boardReader.ReadCurrentAsync(_clubA, Arg.Any<CancellationToken>()).Returns(week);

        await CreateHandler().Handle(new RecordMissionClaimsCommand(), CancellationToken.None);

        _added.Should().HaveCount(2);
        _added.Should().AllSatisfy(c =>
        {
            c.ClubId.Should().Be(_clubA);
            c.PeriodStart.Should().Be(week.PeriodStart);
            c.PeriodEnd.Should().Be(week.PeriodEnd);
        });
        _added.Single(c => c.UserId == "alice").CompletionTime.Should().Be(TimeSpan.FromMinutes(30));
        _added.Single(c => c.UserId == "bob").CompletedAt.Should().BeNull();
    }

    [Fact]
    public async Task Handle_StepsOverAFreeMission_ToTheClaimedOnesAfterIt()
    {
        // A failure here would be swallowed by the per-club catch, so only the claim itself shows it.
        _boardReader.ReadCurrentAsync(_clubA, Arg.Any<CancellationToken>())
            .Returns(Week(Board(1, Tile(index: 0), Tile(index: 1, claimedBy: "alice"))));

        await CreateHandler().Handle(new RecordMissionClaimsCommand(), CancellationToken.None);

        _added.Should().ContainSingle(c => c.UserId == "alice");
    }

    [Fact]
    public async Task Handle_SkipsAMissionWithoutAClaimTime()
    {
        _boardReader.ReadCurrentAsync(_clubA, Arg.Any<CancellationToken>())
            .Returns(Week(Board(1, Tile(claimedBy: "alice") with { ClaimedAt = null }, Tile(index: 1, claimedBy: "bob"))));

        await CreateHandler().Handle(new RecordMissionClaimsCommand(), CancellationToken.None);

        _added.Should().ContainSingle(c => c.UserId == "bob");
    }

    [Fact]
    public async Task Handle_IgnoresACompletionTime_OnAMissionThatIsNotCompleted()
    {
        var tile = Tile(claimedBy: "alice", completed: true) with { Completed = false };
        _boardReader.ReadCurrentAsync(_clubA, Arg.Any<CancellationToken>()).Returns(Week(Board(1, tile)));

        await CreateHandler().Handle(new RecordMissionClaimsCommand(), CancellationToken.None);

        _added.Should().ContainSingle().Which.CompletedAt.Should().BeNull();
    }

    [Fact]
    public async Task Handle_TakesOverANewClaimant_OnARecordedMission()
    {
        var claimedAt = PeriodStart.AddHours(2);
        var tile = Tile(claimedBy: "bob", claimedAt: claimedAt);
        var recorded = MissionClaim.Create(_clubA, PeriodStart, PeriodStart.AddDays(7), tile.MissionId, "alice", claimedAt.AddHours(-1), null);
        _claims.ReadForUpdateByPeriodAsync(_clubA, PeriodStart, Arg.Any<CancellationToken>()).Returns([recorded]);
        _boardReader.ReadCurrentAsync(_clubA, Arg.Any<CancellationToken>()).Returns(Week(Board(1, tile)));

        await CreateHandler().Handle(new RecordMissionClaimsCommand(), CancellationToken.None);

        recorded.UserId.Should().Be("bob");
        recorded.ClaimedAt.Should().Be(claimedAt);
        _added.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_UpdatesARecordedClaim_OnceTheMissionIsCompleted()
    {
        var claimedAt = PeriodStart.AddHours(2);
        var tile = Tile(claimedBy: "alice", claimedAt: claimedAt, completed: true);
        var recorded = MissionClaim.Create(_clubA, PeriodStart, PeriodStart.AddDays(7), tile.MissionId, "alice", claimedAt, null);
        _claims.ReadForUpdateByPeriodAsync(_clubA, PeriodStart, Arg.Any<CancellationToken>()).Returns([recorded]);
        _boardReader.ReadCurrentAsync(_clubA, Arg.Any<CancellationToken>()).Returns(Week(Board(1, tile)));

        await CreateHandler().Handle(new RecordMissionClaimsCommand(), CancellationToken.None);

        _added.Should().BeEmpty();
        recorded.CompletedAt.Should().Be(tile.CompletedAt);
    }

    [Fact]
    public async Task Handle_ReadsThePreviousWeekToo()
    {
        _boardReader.ReadPreviousAsync(_clubA, Arg.Any<CancellationToken>())
            .Returns(Week(Board(1, Tile(claimedBy: "alice", completed: true))));

        await CreateHandler().Handle(new RecordMissionClaimsCommand(), CancellationToken.None);

        _added.Should().ContainSingle(c => c.UserId == "alice");
    }

    [Fact]
    public async Task Handle_KeepsRecordingTheOtherClubs_WhenOneClubFails()
    {
        _boardReader.ReadPreviousAsync(_clubA, Arg.Any<CancellationToken>()).ThrowsAsync(new HttpRequestException("boom"));
        _boardReader.ReadCurrentAsync(_clubB, Arg.Any<CancellationToken>())
            .Returns(Week(Board(1, Tile(claimedBy: "bob"))));

        await CreateHandler().Handle(new RecordMissionClaimsCommand(), CancellationToken.None);

        _added.Should().ContainSingle(c => c.ClubId == _clubB);
        await _claims.Received(1).DeleteClaimedBeforeAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_StopsAtACancellation_InsteadOfTreatingItAsAClubFailure()
    {
        _boardReader.ReadPreviousAsync(_clubA, Arg.Any<CancellationToken>()).ThrowsAsync(new OperationCanceledException());

        var act = () => CreateHandler().Handle(new RecordMissionClaimsCommand(), CancellationToken.None);

        await act.Should().ThrowAsync<OperationCanceledException>();
        await _boardReader.DidNotReceive().ReadPreviousAsync(_clubB, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_DeletesOnlyClaimsOlderThanTheRetention()
    {
        await CreateHandler().Handle(new RecordMissionClaimsCommand(), CancellationToken.None);

        var cutoff = DateTimeOffset.UtcNow.AddDays(-30);
        await _claims.Received(1).DeleteClaimedBeforeAsync(
            Arg.Is<DateTimeOffset>(d => d > cutoff.AddMinutes(-1) && d <= cutoff),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_DoesNothing_WhenTrackingIsDisabled()
    {
        await CreateHandler(enabled: false).Handle(new RecordMissionClaimsCommand(), CancellationToken.None);

        await _boardReader.DidNotReceiveWithAnyArgs().ReadCurrentAsync(default);
        await _claims.DidNotReceiveWithAnyArgs().DeleteClaimedBeforeAsync(default);
    }
}
