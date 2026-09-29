using Entities;
using FluentAssertions;
using GeoClubBot.Tests.TestBuilders;
using NSubstitute;
using UseCases.OutputPorts.Repositories;
using UseCases.UseCases.DailyActivity;
using Xunit;

namespace GeoClubBot.Tests.Application.UseCases.DailyActivityTests;

public sealed class GetDailyStreaksHandlerTests
{
    private static readonly Guid ClubId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly IClubMemberDailyActivityRepository _dailyActivities = Substitute.For<IClubMemberDailyActivityRepository>();
    private readonly IClubMemberRepository _members = Substitute.For<IClubMemberRepository>();

    private readonly DateOnly _today = DateOnly.FromDateTime(DateTime.UtcNow);

    public GetDailyStreaksHandlerTests()
    {
        _dailyActivities.ReadDailyActivitiesAsync(
                Arg.Any<Guid?>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns([]);
        _members.ReadClubMembersByClubIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns([]);
    }

    private GetDailyStreaksHandler CreateHandler() => new(_dailyActivities, _members);

    private void ArrangeDays(params ClubMemberDailyActivity[] rows) =>
        _dailyActivities.ReadDailyActivitiesAsync(
                Arg.Any<Guid?>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns([.. rows]);

    private void ArrangeMembers(params ClubMember[] members) =>
        _members.ReadClubMembersByClubIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns([.. members]);

    /// <summary>A day on which the member played the daily challenge or a duel (or didn't, with 0).</summary>
    private static ClubMemberDailyActivity Played(string userId, DateOnly date, int count = 1) =>
        ClubMemberDailyActivity.Create(ClubId, userId, date, dailyChallengeCount: count);

    /// <summary>A row written before the daily challenge was tracked: only the old mission count is known.</summary>
    private static ClubMemberDailyActivity Legacy(string userId, DateOnly date, int missionCount) =>
        ClubMemberDailyActivity.Create(ClubId, userId, date, dailyChallengeCount: null, legacyDailyMissionCount: missionCount);

    private static ClubMember Member(string userId, string nickname) =>
        new ClubMemberBuilder().WithUserId(userId).WithNickname(nickname).InClub(ClubId).Build();

    [Fact]
    public async Task Handle_ReturnsEmpty_WhenNoDays()
    {
        var result = await CreateHandler().Handle(new GetDailyStreaksQuery(ClubId, 30), CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_CountsConsecutiveDaysEndingToday()
    {
        ArrangeMembers(Member("u1", "Alice"));
        ArrangeDays(
            Played("u1", _today),
            Played("u1", _today.AddDays(-1)),
            Played("u1", _today.AddDays(-2)));

        var result = await CreateHandler().Handle(new GetDailyStreaksQuery(ClubId, 30), CancellationToken.None);

        result.Should().ContainSingle();
        result[0].Nickname.Should().Be("Alice");
        result[0].CurrentStreak.Should().Be(3);
        result[0].LongestStreak.Should().Be(3);
    }

    [Fact]
    public async Task Handle_AllowsGrace_WhenTodayNotYetSnapshotted()
    {
        ArrangeMembers(Member("u1", "Alice"));
        ArrangeDays(
            Played("u1", _today.AddDays(-1)),
            Played("u1", _today.AddDays(-2)));

        var result = await CreateHandler().Handle(new GetDailyStreaksQuery(ClubId, 30), CancellationToken.None);

        result[0].CurrentStreak.Should().Be(2);
    }

    [Fact]
    public async Task Handle_BreaksCurrentStreakOnGap_ButLongestCapturesBestRun()
    {
        ArrangeMembers(Member("u1", "Alice"));
        ArrangeDays(
            // Current run is just today (a gap sits at yesterday).
            Played("u1", _today),
            // An older run of three consecutive days.
            Played("u1", _today.AddDays(-3)),
            Played("u1", _today.AddDays(-4)),
            Played("u1", _today.AddDays(-5)));

        var result = await CreateHandler().Handle(new GetDailyStreaksQuery(ClubId, 30), CancellationToken.None);

        result[0].CurrentStreak.Should().Be(1);
        result[0].LongestStreak.Should().Be(3);
    }

    [Fact]
    public async Task Handle_IgnoresDaysWithoutAChallengeOrDuel()
    {
        ArrangeMembers(Member("u1", "Alice"));
        ArrangeDays(
            Played("u1", _today),
            Played("u1", _today.AddDays(-1), count: 0),
            Played("u1", _today.AddDays(-2)));

        var result = await CreateHandler().Handle(new GetDailyStreaksQuery(ClubId, 30), CancellationToken.None);

        result[0].CurrentStreak.Should().Be(1);
        result[0].LongestStreak.Should().Be(1);
    }

    [Fact]
    public async Task Handle_SkipsDepartedMembers_WithoutNicknames()
    {
        ArrangeMembers(Member("u1", "Alice"));
        ArrangeDays(
            Played("u1", _today),
            // u2 has days but is no longer a club member, so it can't be named.
            Played("u2", _today));

        var result = await CreateHandler().Handle(new GetDailyStreaksQuery(ClubId, 30), CancellationToken.None);

        result.Should().ContainSingle().Which.Nickname.Should().Be("Alice");
    }

    [Fact]
    public async Task Handle_RanksByCurrentThenLongestThenNickname()
    {
        ArrangeMembers(Member("u1", "Alice"), Member("u2", "Bob"), Member("u3", "Cara"));
        ArrangeDays(
            // Alice: current 1, longest 1.
            Played("u1", _today),
            // Bob: current 2, longest 2.
            Played("u2", _today),
            Played("u2", _today.AddDays(-1)),
            // Cara: current 2, longest 5 (older run).
            Played("u3", _today),
            Played("u3", _today.AddDays(-1)),
            Played("u3", _today.AddDays(-4)),
            Played("u3", _today.AddDays(-5)),
            Played("u3", _today.AddDays(-6)),
            Played("u3", _today.AddDays(-7)),
            Played("u3", _today.AddDays(-8)));

        var result = await CreateHandler().Handle(new GetDailyStreaksQuery(ClubId, 30), CancellationToken.None);

        result.Select(s => s.Nickname).Should().ContainInOrder("Cara", "Bob", "Alice");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task Handle_ClampsNonPositiveWindow_ToAtLeastOneDay(int windowDays)
    {
        ArrangeMembers(Member("u1", "Alice"));
        ArrangeDays(Played("u1", _today));

        var result = await CreateHandler().Handle(new GetDailyStreaksQuery(ClubId, windowDays), CancellationToken.None);

        result.Should().ContainSingle().Which.CurrentStreak.Should().Be(1);
    }

    [Fact]
    public async Task Handle_NeedsOnlyTheChallengeOrDuel_NowThatTheDailyMissionIsGone()
    {
        // Since the mission board replaced the daily mission, a day with the challenge played and
        // no mission (the old second award) still extends the streak.
        ArrangeMembers(Member("u1", "Alice"));
        ArrangeDays(
            ClubMemberDailyActivity.Create(ClubId, "u1", _today, dailyChallengeCount: 1, legacyDailyMissionCount: 0),
            ClubMemberDailyActivity.Create(ClubId, "u1", _today.AddDays(-1), dailyChallengeCount: 1, legacyDailyMissionCount: 0));

        var result = await CreateHandler().Handle(new GetDailyStreaksQuery(ClubId, 30), CancellationToken.None);

        result.Single().CurrentStreak.Should().Be(2);
    }

    [Fact]
    public async Task Handle_BreaksTheStreak_OnATrackedDayWithOnlyTheOldMissionDone()
    {
        // Once the challenge is tracked, it alone decides: the old daily mission never kept a
        // streak on its own.
        ArrangeMembers(Member("u1", "Alice"));
        ArrangeDays(
            Played("u1", _today),
            ClubMemberDailyActivity.Create(ClubId, "u1", _today.AddDays(-1), dailyChallengeCount: 0, legacyDailyMissionCount: 1),
            Played("u1", _today.AddDays(-2)));

        var result = await CreateHandler().Handle(new GetDailyStreaksQuery(ClubId, 30), CancellationToken.None);

        result.Single().CurrentStreak.Should().Be(1);
    }

    [Fact]
    public async Task Handle_KeepsLegacyDaysIntact_WhenTheChallengeWasNotYetTracked()
    {
        // Rows predating the tracking hold null, which means "unknown", not "did not happen" -
        // those days are judged on the old daily mission, or every historical streak would collapse.
        ArrangeMembers(Member("u1", "Alice"));
        ArrangeDays(
            Played("u1", _today),
            Legacy("u1", _today.AddDays(-1), missionCount: 1),
            Legacy("u1", _today.AddDays(-2), missionCount: 1),
            Legacy("u1", _today.AddDays(-3), missionCount: 0));

        var result = await CreateHandler().Handle(new GetDailyStreaksQuery(ClubId, 30), CancellationToken.None);

        result.Single().CurrentStreak.Should().Be(3);
    }
}
