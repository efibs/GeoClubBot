using Configuration;
using Entities;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.UseCases.ClubMemberActivity;
using Xunit;

namespace GeoClubBot.Tests.Integration.UseCases;

/// <summary>
/// Exercises the read-side club-member-activity use cases (average-XP leaderboard, club / player
/// statistics, last-check time, this-week activity) through the real MediatR pipeline. The history
/// and excuse data live in Postgres; the GeoGuessr activity feed is substituted.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class ClubMemberActivityUseCaseIntegrationTests(PostgresFixture fixture)
{
    private static string NewUserId() => Guid.NewGuid().ToString("N")[..24];
    private static string NewNickname() => $"nick-{Guid.NewGuid():N}"[..30];

    private MediatorTestHost CreateHost() => new(fixture.ConnectionString);

    private MediatorTestHost CreateHost(Guid mainClubId) =>
        new(fixture.ConnectionString, services =>
            services.AddSingleton(Options.Create(new GeoGuessrConfiguration
            {
                SyncSchedule = "0 0 0 * * ?",
                ActivityNcfaToken = "x",
                MissionsNcfaToken = "x",
                UserProfileNcfaToken = "x",
                Clubs = [new GeoGuessrClubEntry { ClubId = mainClubId, NcfaToken = "x", IsMain = true }],
            })));

    private async Task<(string userId, string nickname)> SeedMemberWithHistoryAsync(Guid clubId)
    {
        var userId = NewUserId();
        var nickname = NewNickname();

        await using var seed = fixture.CreateDbContext();
        if (!seed.Clubs.Any(c => c.ClubId == clubId))
        {
            seed.Add(Club.Create(clubId, $"club-{clubId:N}", 1));
        }
        var user = GeoGuessrUser.Create(userId, nickname);
        seed.Add(user);
        seed.Add(ClubMember.Create(user, clubId, xp: 400, joinedAt: DateTimeOffset.UtcNow.AddMonths(-3)));
        seed.Add(ClubMemberHistoryEntry.Create(userId, clubId, xp: 100, DateTimeOffset.UtcNow.AddDays(-20)));
        seed.Add(ClubMemberHistoryEntry.Create(userId, clubId, xp: 250, DateTimeOffset.UtcNow.AddDays(-10)));
        seed.Add(ClubMemberHistoryEntry.Create(userId, clubId, xp: 400, DateTimeOffset.UtcNow.AddDays(-1)));
        await seed.SaveChangesAsync();

        return (userId, nickname);
    }

    [Fact]
    public async Task CalculateAverageXp_AveragesTheXpDifferences()
    {
        var clubId = Guid.NewGuid();
        var (_, nickname) = await SeedMemberWithHistoryAsync(clubId);

        using var host = CreateHost();
        var leaderboard = await host.SendAsync(new CalculateAverageXpQuery(clubId, HistoryDepth: 2));

        // XP history (desc): 400, 250, 100 → diffs 150 and 150 → average 150.
        leaderboard.Should().ContainSingle(m => m.Nickname == nickname)
            .Which.AverageXp.Should().Be(150);
    }

    [Fact]
    public async Task PlayerStatistics_ReturnsStatistics_ForAKnownMember()
    {
        var clubId = Guid.NewGuid();
        var (_, nickname) = await SeedMemberWithHistoryAsync(clubId);

        using var host = CreateHost(Guid.NewGuid());
        var stats = await host.SendAsync(new PlayerStatisticsQuery(nickname));

        stats.Should().NotBeNull();
        stats!.Nickname.Should().Be(nickname);
    }

    [Fact]
    public async Task PlayerStatistics_ReturnsNull_ForUnknownNickname()
    {
        using var host = CreateHost(Guid.NewGuid());

        var stats = await host.SendAsync(new PlayerStatisticsQuery($"missing-{Guid.NewGuid():N}"[..20]));

        stats.Should().BeNull();
    }

    [Fact]
    public async Task ClubStatistics_ReturnsStatistics_WhenTheMainClubHasHistory()
    {
        var mainClubId = Guid.NewGuid();
        await SeedMemberWithHistoryAsync(mainClubId);

        using var host = CreateHost(mainClubId);
        var stats = await host.SendAsync(new ClubStatisticsQuery());

        stats.Should().NotBeNull();
    }

    [Fact]
    public async Task GetLastCheckTime_ReturnsTheStoredTime()
    {
        var mainClubId = Guid.NewGuid();
        var checkTime = DateTimeOffset.UtcNow.AddHours(-3);
        await using (var seed = fixture.CreateDbContext())
        {
            seed.Add(Club.Create(mainClubId, $"club-{mainClubId:N}", 1, checkTime));
            await seed.SaveChangesAsync();
        }

        using var host = CreateHost(mainClubId);
        var lastCheck = await host.SendAsync(new GetLastCheckTimeQuery());

        lastCheck.Should().NotBeNull();
        lastCheck!.Value.Should().BeCloseTo(checkTime, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task GetActivityThisWeek_ReturnsZero_WhenUserHasNoMembership()
    {
        using var host = CreateHost(Guid.NewGuid());

        var activity = await host.SendAsync(new GetActivityThisWeekQuery(NewUserId()));

        activity.TotalXp.Should().Be(0);
    }

    [Fact]
    public async Task GetActivityThisWeek_SumsTheMembersActivities()
    {
        var clubId = Guid.NewGuid();
        var userId = NewUserId();
        await using (var seed = fixture.CreateDbContext())
        {
            seed.Add(Club.Create(clubId, $"club-{clubId:N}", 1));
            var user = GeoGuessrUser.Create(userId, NewNickname());
            seed.Add(user);
            seed.Add(ClubMember.Create(user, clubId, xp: 0, joinedAt: DateTimeOffset.UtcNow.AddMonths(-3)));
            await seed.SaveChangesAsync();
        }

        using var host = CreateHost(Guid.NewGuid());
        // Default DailyMissionReminderConfiguration.DailyMissionXpReward is 20.
        host.Mock<IGeoGuessrActivityReader>()
            .ReadActivitiesSinceAsync(clubId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<ReadClubActivitiesItemDto>)
            [
                new ReadClubActivitiesItemDto { UserId = userId, XpReward = 20, RecordedAt = DateTimeOffset.UtcNow },
            ]);

        var activity = await host.SendAsync(new GetActivityThisWeekQuery(userId));

        activity.TotalXp.Should().Be(20);
    }

    [Fact]
    public async Task ActivityLeaderboard_ReturnsTheRankedMembers_ForANamedClub()
    {
        var clubId = Guid.NewGuid();
        var (_, nickname) = await SeedMemberWithHistoryAsync(clubId);
        var clubName = $"club-{clubId:N}";

        using var host = CreateHost(Guid.NewGuid());
        var result = await host.SendAsync(new GetActivityLeaderboardQuery(clubName, HistoryDepth: 2));

        result.ClubName.Should().Be(clubName);
        result.Leaderboard.Should().NotBeNull();
        result.Leaderboard!.Should().Contain(m => m.Nickname == nickname);
    }

    /// <summary>
    /// Seeds a member whose <em>current</em> club is <paramref name="currentClubId"/> (which may be
    /// null = no longer in any club) but whose history rows are tagged with <paramref name="historyClubId"/>
    /// — i.e. they earned that XP while still in that club before switching/leaving.
    /// </summary>
    private async Task<string> SeedMemberWithHistoryTaggedAsync(
        string nickname, Guid? currentClubId, Guid historyClubId, params int[] xps)
    {
        var userId = NewUserId();

        await using var seed = fixture.CreateDbContext();
        foreach (var c in new[] { currentClubId, historyClubId }.OfType<Guid>().Distinct())
        {
            if (!seed.Clubs.Any(x => x.ClubId == c))
            {
                seed.Add(Club.Create(c, $"club-{c:N}", 1));
            }
        }

        var user = GeoGuessrUser.Create(userId, nickname);
        seed.Add(user);
        seed.Add(ClubMember.Create(user, currentClubId, xp: xps[^1], joinedAt: DateTimeOffset.UtcNow.AddMonths(-3)));

        var timestamp = DateTimeOffset.UtcNow.AddDays(-5 * xps.Length);
        foreach (var xp in xps)
        {
            seed.Add(ClubMemberHistoryEntry.Create(userId, historyClubId, xp, timestamp));
            timestamp = timestamp.AddDays(5);
        }

        await seed.SaveChangesAsync();
        return userId;
    }

    [Fact]
    public async Task CalculateAverageXp_ExcludesAFormerMember_WhoSwitchedToAnotherClub()
    {
        var clubA = Guid.NewGuid();
        var clubB = Guid.NewGuid();

        var currentNickname = NewNickname();
        var formerNickname = NewNickname();

        // Currently in club A, earned their XP in club A.
        await SeedMemberWithHistoryTaggedAsync(currentNickname, currentClubId: clubA, historyClubId: clubA, 100, 250, 400);
        // Earned XP in club A but has since switched to club B — must NOT appear in club A's averages.
        await SeedMemberWithHistoryTaggedAsync(formerNickname, currentClubId: clubB, historyClubId: clubA, 100, 300, 500);

        using var host = CreateHost();
        var leaderboard = await host.SendAsync(new CalculateAverageXpQuery(clubA, HistoryDepth: 2));

        leaderboard.Should().Contain(m => m.Nickname == currentNickname);
        leaderboard.Should().NotContain(m => m.Nickname == formerNickname,
            "a member who switched to another club is no longer in club A");
    }

    [Fact]
    public async Task CalculateAverageXp_ExcludesAFormerMember_WhoLeftAllClubs()
    {
        var clubA = Guid.NewGuid();

        var currentNickname = NewNickname();
        var leaverNickname = NewNickname();

        await SeedMemberWithHistoryTaggedAsync(currentNickname, currentClubId: clubA, historyClubId: clubA, 100, 250, 400);
        // No current club (left entirely) but history is still tagged with club A.
        await SeedMemberWithHistoryTaggedAsync(leaverNickname, currentClubId: null, historyClubId: clubA, 100, 300, 500);

        using var host = CreateHost();
        var leaderboard = await host.SendAsync(new CalculateAverageXpQuery(clubA, HistoryDepth: 2));

        leaderboard.Should().Contain(m => m.Nickname == currentNickname);
        leaderboard.Should().NotContain(m => m.Nickname == leaverNickname,
            "a member who left all clubs is no longer in club A");
    }
}
