using Configuration;
using Entities;
using FluentAssertions;
using GeoClubBot.Tests.TestBuilders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.OutputPorts.Repositories;
using UseCases.UseCases.ClubMemberActivity;
using Xunit;
using static GeoClubBot.Tests.TestBuilders.MissionBoards;

namespace GeoClubBot.Tests.Application.UseCases.ClubMemberActivity;

/// <summary>
/// Unit tests for <see cref="ActivityReadHandlers"/>. The last check time is read from the main
/// club's <c>LatestActivityCheckTime</c> (recorded by the activity check on every run) — the value
/// that drives which excuses count as relevant (issue #200). The per-member activity views are
/// covered here too: streak and board missions per day, the requirement progress of the current
/// check period, and the (unverified) help counts.
/// </summary>
public sealed class ActivityReadHandlersTests
{
    private readonly IClubRepository _clubs = Substitute.For<IClubRepository>();
    private readonly IClubMemberRepository _clubMembers = Substitute.For<IClubMemberRepository>();
    private readonly IGeoGuessrActivityReader _activityReader = Substitute.For<IGeoGuessrActivityReader>();
    private readonly IClubMissionBoardReader _boardReader = Substitute.For<IClubMissionBoardReader>();
    private readonly Guid _mainClubId = Guid.NewGuid();

    private ActivityReadHandlers CreateHandler(bool showHelps = true)
    {
        var geoGuessrConfig = Options.Create(new GeoGuessrConfiguration
        {
            SyncSchedule = "0 0 0 * * ?",
            ActivityNcfaToken = "x",
            UserProfileNcfaToken = "x",
            Clubs = [new GeoGuessrClubEntry { ClubId = _mainClubId, NcfaToken = "x", IsMain = true }],
        });
        var activityChecker = new ActivityCheckerConfigurationBuilder()
            .WithMinXp(0)
            .WithRequirements(("DailyChallengeOrDuel", 6), ("BoardMission", 2))
            .WithRuleXp(["BoardClearBonus"], ("BoardMission", 3))
            .BuildOptions();

        return new ActivityReadHandlers(
            _clubs, _clubMembers, _activityReader, _boardReader, ClubActivities.Classifier(), geoGuessrConfig,
            activityChecker, Options.Create(new ActivityViewsConfiguration { ShowHelps = showHelps }),
            NullLogger<ActivityReadHandlers>.Instance);
    }

    [Fact]
    public async Task GetLastCheckTime_ReturnsTheMainClubsRecordedCheckTime()
    {
        var checkTime = DateTimeOffset.UtcNow.AddDays(-1);
        _clubs.ReadClubByIdAsync(_mainClubId, Arg.Any<CancellationToken>())
            .Returns(Entities.Club.Create(_mainClubId, "main", 1, checkTime));

        var result = await CreateHandler().Handle(new GetLastCheckTimeQuery(), CancellationToken.None);

        result.Should().Be(checkTime);
    }

    [Fact]
    public async Task GetLastCheckTime_ReturnsNull_WhenTheClubWasNeverChecked()
    {
        _clubs.ReadClubByIdAsync(_mainClubId, Arg.Any<CancellationToken>())
            .Returns(Entities.Club.Create(_mainClubId, "main", 1));

        var result = await CreateHandler().Handle(new GetLastCheckTimeQuery(), CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetLastCheckTime_ReturnsNull_WhenTheMainClubDoesNotExist()
    {
        _clubs.ReadClubByIdAsync(_mainClubId, Arg.Any<CancellationToken>()).Returns((Entities.Club?)null);

        var result = await CreateHandler().Handle(new GetLastCheckTimeQuery(), CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetActivityLastDays_CountsStreakMissionsAndBonusSeparately()
    {
        var userId = "user-1";
        ArrangeMember(userId);
        ArrangeActivities(
            ClubActivities.Challenge(userId),
            ClubActivities.BoardMission(userId),
            ClubActivities.BoardMission(userId),
            ClubActivities.BoardBonus(userId),
            ClubActivities.ClubChallenge(userId),
            ClubActivities.Untyped(userId, xpReward: 150));

        var result = await CreateHandler().Handle(new GetActivityLastDaysQuery(userId, DaysBack: 7), CancellationToken.None);

        result.TotalXp.Should().Be(20 + 40 + 100 + 150, "every kind of XP counts towards the total");
        result.StreakDays.Should().Be(1);
        result.BoardMissions.Should().Be(2);
        result.BoardClearBonusXp.Should().Be(100);
        result.Days.Should().HaveCount(7);
        result.Days[^1].ChallengeDone.Should().BeTrue();
        result.Days[^1].BoardMissions.Should().Be(2);
    }

    [Fact]
    public async Task GetActivityLastDays_HasNoRuleXpOrRequirements()
    {
        // The weekly caps have no meaning over an arbitrary number of days.
        var userId = "user-1";
        ArrangeMember(userId);
        ArrangeActivities(ClubActivities.Challenge(userId));

        var result = await CreateHandler().Handle(new GetActivityLastDaysQuery(userId, DaysBack: 7), CancellationToken.None);

        result.RuleXp.Should().BeNull();
        result.RequirementResults.Should().BeEmpty();
    }

    [Fact]
    public async Task GetActivityLastDays_IgnoresOtherMembersActivity()
    {
        var userId = "user-1";
        ArrangeMember(userId);
        ArrangeActivities(ClubActivities.BoardMission(userId), ClubActivities.BoardMission("someone-else"));

        var result = await CreateHandler().Handle(new GetActivityLastDaysQuery(userId, DaysBack: 7), CancellationToken.None);

        result.TotalXp.Should().Be(20);
        result.BoardMissions.Should().Be(1);
    }

    [Fact]
    public async Task GetActivityThisWeek_ShowsProgressTowardsTheClubsRequirements_SinceTheLastCheck()
    {
        var userId = "user-1";
        var lastCheck = DateTimeOffset.UtcNow.AddDays(-3);
        _clubs.ReadClubByIdAsync(_mainClubId, Arg.Any<CancellationToken>())
            .Returns(Entities.Club.Create(_mainClubId, "main", 1, lastCheck));
        ArrangeMember(userId);
        ArrangeActivities(
            ClubActivities.Challenge(userId, lastCheck.AddHours(1)),
            ClubActivities.Challenge(userId, lastCheck.AddDays(1)),
            // Before the last check: belongs to the previous week.
            ClubActivities.Challenge(userId, lastCheck.AddHours(-1)),
            ClubActivities.BoardMission(userId, lastCheck.AddHours(2)),
            ClubActivities.BoardBonus(userId, lastCheck.AddHours(2)));

        var result = await CreateHandler().Handle(new GetActivityThisWeekQuery(userId), CancellationToken.None);

        result.PeriodStart.Should().Be(lastCheck);
        result.StreakDays.Should().Be(2);
        result.RuleXp.Should().Be(60, "the board bonus is not rule XP");
        result.RequirementResults.Should().BeEquivalentTo(new[]
        {
            new ActivityRequirementResult(ClubXpActivityKind.DailyChallengeOrDuel, 2, 6, 6),
            new ActivityRequirementResult(ClubXpActivityKind.BoardMission, 1, 2, 2)
        });
        result.AllRequirementsMet.Should().BeFalse();
    }

    [Fact]
    public async Task GetActivityThisWeek_IsBoundedByTheFeedLookback_WhenTheClubWasNeverChecked()
    {
        var userId = "user-1";
        _clubs.ReadClubByIdAsync(_mainClubId, Arg.Any<CancellationToken>())
            .Returns(Entities.Club.Create(_mainClubId, "main", 1));
        ArrangeMember(userId);
        ArrangeActivities();

        var result = await CreateHandler().Handle(new GetActivityThisWeekQuery(userId), CancellationToken.None);

        result.PeriodStart.Should().BeCloseTo(DateTimeOffset.UtcNow.AddDays(-8), TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task GetActivityLastDays_CountsHelpsFromThisAndLastBoardWeek()
    {
        var userId = "user-1";
        ArrangeMember(userId);
        ArrangeActivities();
        _boardReader.ReadCurrentAsync(_mainClubId, Arg.Any<CancellationToken>())
            .Returns(Week(Board(1, Tile(claimedBy: "a", helpers: [userId]), Tile(claimedBy: "b", helpers: [userId]))));
        _boardReader.ReadPreviousAsync(_mainClubId, Arg.Any<CancellationToken>())
            .Returns(Week(Board(1, Tile(claimedBy: "a", helpers: [userId]))));

        var result = await CreateHandler().Handle(new GetActivityLastDaysQuery(userId, DaysBack: 7), CancellationToken.None);

        result.HelpedThisWeek.Should().Be(2);
        result.HelpedLastWeek.Should().Be(1);
    }

    [Fact]
    public async Task GetActivityLastDays_LeavesHelpsOut_WhenConfiguredOff()
    {
        var userId = "user-1";
        ArrangeMember(userId);
        ArrangeActivities();

        var result = await CreateHandler(showHelps: false).Handle(new GetActivityLastDaysQuery(userId, DaysBack: 7), CancellationToken.None);

        result.HelpedThisWeek.Should().BeNull();
        await _boardReader.DidNotReceive().ReadCurrentAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    private void ArrangeMember(string userId) =>
        _clubMembers.ReadClubMemberByUserIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(new ClubMemberBuilder()
                .WithUserId(userId)
                .InClub(_mainClubId)
                .JoinedAt(DateTimeOffset.UtcNow.AddMonths(-3))
                .Build());

    private void ArrangeActivities(params ReadClubActivitiesItemDto[] activities) =>
        _activityReader
            .ReadActivitiesSinceAsync(_mainClubId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(activities);
}
