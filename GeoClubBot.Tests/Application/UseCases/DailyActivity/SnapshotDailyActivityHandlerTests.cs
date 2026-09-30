using Configuration;
using Entities;
using FluentAssertions;
using GeoClubBot.Tests.TestBuilders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.OutputPorts.Repositories;
using UseCases.UseCases.DailyActivity;
using Xunit;

namespace GeoClubBot.Tests.Application.UseCases.DailyActivityTests;

public sealed class SnapshotDailyActivityHandlerTests
{
    private static readonly Guid ClubA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ClubB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly IClubMemberDailyActivityRepository _dailyActivities = Substitute.For<IClubMemberDailyActivityRepository>();
    private readonly IClubMemberRepository _members = Substitute.For<IClubMemberRepository>();
    private readonly IGeoGuessrActivityReader _activityReader = Substitute.For<IGeoGuessrActivityReader>();
    private readonly ILogger<SnapshotDailyActivityHandler> _logger =
        Substitute.For<ILogger<SnapshotDailyActivityHandler>>();

    private readonly DateOnly _yesterday = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);

    public SnapshotDailyActivityHandlerTests()
    {
        _members.ReadClubMembersByClubIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns([]);
        _activityReader.ReadActivitiesSinceAsync(Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns([]);
    }

    private SnapshotDailyActivityHandler CreateHandler(params Guid[] clubIds) => new(
        _dailyActivities,
        _members,
        _activityReader,
        ClubActivities.Classifier(),
        Options.Create(new GeoGuessrConfiguration
        {
            SyncSchedule = "0 0 0 * * ?",
            ActivityNcfaToken = "x",
            UserProfileNcfaToken = "x",
            Clubs = clubIds
                .Select((id, i) => new GeoGuessrClubEntry { ClubId = id, NcfaToken = "x", IsMain = i == 0 })
                .ToList(),
        }),
        _logger);

    private DateTimeOffset YesterdayAt(int hour) =>
        new(_yesterday.ToDateTime(new TimeOnly(hour, 0)), TimeSpan.Zero);

    [Fact]
    public async Task Handle_WritesOneRowPerMember_CountingYesterdaysActivityByKind()
    {
        var memberDone = new ClubMemberBuilder().WithUserId("user-done-000000000000000").InClub(ClubA).Build();
        var memberIdle = new ClubMemberBuilder().WithUserId("user-idle-000000000000000").InClub(ClubA).Build();
        _members.ReadClubMembersByClubIdAsync(ClubA, Arg.Any<CancellationToken>())
            .Returns([memberDone, memberIdle]);

        _activityReader.ReadActivitiesSinceAsync(ClubA, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns([
                ClubActivities.Challenge(memberDone.UserId, YesterdayAt(8)),
                // Same 20 XP as the challenge, but a board mission - counted separately.
                ClubActivities.BoardMission(memberDone.UserId, YesterdayAt(12)),
                ClubActivities.BoardMission(memberDone.UserId, YesterdayAt(13)),
                ClubActivities.BoardBonus(memberDone.UserId, YesterdayAt(13)),
                // A board mission, but recorded today, i.e. outside the snapshotted day.
                ClubActivities.BoardMission(memberIdle.UserId, YesterdayAt(8).AddDays(1)),
            ]);

        IEnumerable<ClubMemberDailyActivity>? written = null;
        _dailyActivities.AddRange(Arg.Do<IEnumerable<ClubMemberDailyActivity>>(rows => written = rows.ToList()));

        await CreateHandler(ClubA).Handle(new SnapshotDailyActivityCommand(), CancellationToken.None);

        written.Should().NotBeNull();
        written.Should().HaveCount(2);
        written.Should().OnlyContain(r => r.ClubId == ClubA && r.Date == _yesterday);

        var doneRow = written.Single(r => r.UserId == memberDone.UserId);
        doneRow.DailyChallengeCount.Should().Be(1);
        doneRow.BoardMissionCount.Should().Be(2);
        doneRow.BoardClearBonusCount.Should().Be(1);
        doneRow.Xp.Should().Be(160);
        doneRow.LegacyDailyMissionCount.Should().Be(0);
        doneRow.ExtendsStreak.Should().BeTrue();

        var idleRow = written.Single(r => r.UserId == memberIdle.UserId);
        idleRow.DailyChallengeCount.Should().Be(0);
        idleRow.BoardMissionCount.Should().Be(0);
        idleRow.Xp.Should().Be(0);
        idleRow.ExtendsStreak.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_SkipsClubsThatAlreadyHaveASnapshot()
    {
        _dailyActivities.HasSnapshotForDayAsync(ClubA, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(true);

        await CreateHandler(ClubA, ClubB).Handle(new SnapshotDailyActivityCommand(), CancellationToken.None);

        await _activityReader.DidNotReceive()
            .ReadActivitiesSinceAsync(ClubA, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        await _activityReader.Received(1)
            .ReadActivitiesSinceAsync(ClubB, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ContinuesWithTheRemainingClubs_WhenOneClubsFeedFails()
    {
        _activityReader.ReadActivitiesSinceAsync(ClubA, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<ReadClubActivitiesItemDto>>(_ => throw new InvalidOperationException("feed down"));

        var member = new ClubMemberBuilder().WithUserId("user-b-00000000000000000").InClub(ClubB).Build();
        _members.ReadClubMembersByClubIdAsync(ClubB, Arg.Any<CancellationToken>())
            .Returns([member]);

        await CreateHandler(ClubA, ClubB).Handle(new SnapshotDailyActivityCommand(), CancellationToken.None);

        _dailyActivities.Received(1).AddRange(
            Arg.Is<IEnumerable<ClubMemberDailyActivity>>(rows => rows!.All(r => r.ClubId == ClubB)));
    }
}
