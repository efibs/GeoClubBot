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

namespace GeoClubBot.Tests.Application.UseCases.ClubMemberActivity;

/// <summary>
/// Unit tests for <see cref="ActivityReadHandlers"/>. The last check time is read from the main
/// club's <c>LatestActivityCheckTime</c> (recorded by the activity check on every run) — the value
/// that drives which excuses count as relevant (issue #200). The per-member activity views are
/// covered here too, notably that they report daily activity only.
/// </summary>
public sealed class ActivityReadHandlersTests
{
    private readonly IClubRepository _clubs = Substitute.For<IClubRepository>();
    private readonly IClubMemberRepository _clubMembers = Substitute.For<IClubMemberRepository>();
    private readonly IGeoGuessrActivityReader _activityReader = Substitute.For<IGeoGuessrActivityReader>();
    private readonly Guid _mainClubId = Guid.NewGuid();

    private ActivityReadHandlers CreateHandler()
    {
        var geoGuessrConfig = Options.Create(new GeoGuessrConfiguration
        {
            SyncSchedule = "0 0 0 * * ?",
            ActivityNcfaToken = "x",
            MissionsNcfaToken = "x",
            UserProfileNcfaToken = "x",
            Clubs = [new GeoGuessrClubEntry { ClubId = _mainClubId, NcfaToken = "x", IsMain = true }],
        });
        return new ActivityReadHandlers(
            _clubs, _clubMembers, _activityReader, ClubActivities.Classifier(), geoGuessrConfig,
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
    public async Task GetActivityLastDays_ExcludesWeeklyMissionXp_FromTheTotal()
    {
        // A weekly mission is worth 1000 XP - fifty times a daily award - so counting it here
        // would swamp the figure and misrepresent how active the member actually was.
        var userId = "user-1";
        ArrangeMember(userId);
        ArrangeActivities(
            ClubActivities.Mission(userId),
            ClubActivities.Challenge(userId),
            ClubActivities.Weekly(userId));

        var result = await CreateHandler().Handle(new GetActivityLastDaysQuery(userId, DaysBack: 7), CancellationToken.None);

        result.TotalXp.Should().Be(40);
    }

    [Fact]
    public async Task GetActivityThisWeek_ExcludesWeeklyMissionXp_FromTheTotal()
    {
        var userId = "user-1";
        ArrangeMember(userId);
        ArrangeActivities(ClubActivities.Mission(userId), ClubActivities.Weekly(userId));

        var result = await CreateHandler().Handle(new GetActivityThisWeekQuery(userId), CancellationToken.None);

        result.TotalXp.Should().Be(20);
    }

    [Fact]
    public async Task GetActivityLastDays_CountsEveryOtherKindOfXp()
    {
        // Only weeklies are filtered out; anything else the feed reports still counts towards
        // the member's daily total, including entries the bot has no name for.
        var userId = "user-1";
        ArrangeMember(userId);
        ArrangeActivities(
            ClubActivities.Mission(userId),
            ClubActivities.ClubChallenge(userId),
            ClubActivities.Untyped(userId, xpReward: 150));

        var result = await CreateHandler().Handle(new GetActivityLastDaysQuery(userId, DaysBack: 7), CancellationToken.None);

        result.TotalXp.Should().Be(170);
    }

    [Fact]
    public async Task GetActivityLastDays_IgnoresOtherMembersActivity()
    {
        var userId = "user-1";
        ArrangeMember(userId);
        ArrangeActivities(ClubActivities.Mission(userId), ClubActivities.Mission("someone-else"));

        var result = await CreateHandler().Handle(new GetActivityLastDaysQuery(userId, DaysBack: 7), CancellationToken.None);

        result.TotalXp.Should().Be(20);
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
