using Configuration;
using Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.OutputPorts.Repositories;
using UseCases.UseCases.DailyMissionStatistics;
using Utilities;
using Xunit;

namespace GeoClubBot.Tests.Integration.UseCases;

/// <summary>
/// Exercises the daily-mission statistics query and the completion snapshot command end-to-end
/// through the real MediatR pipeline against Postgres. The statistics query aggregates over the
/// whole (club-less) <c>DailyMissions</c> table, so those tests clear the missions/completions
/// tables first instead of namespacing by club id like the other integration tests.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class DailyMissionStatisticsUseCaseIntegrationTests(PostgresFixture fixture)
{
    private static string NewUserId() => Guid.NewGuid().ToString("N")[..24];

    private MediatorTestHost CreateHost(params Guid[] clubIds) =>
        new(fixture.ConnectionString, services =>
            services.AddSingleton(Options.Create(new GeoGuessrConfiguration
            {
                SyncSchedule = "0 0 0 * * ?",
                ActivityNcfaToken = "x",
                MissionsNcfaToken = "x",
                UserProfileNcfaToken = "x",
                Clubs = clubIds
                    .Select((id, i) => new GeoGuessrClubEntry { ClubId = id, NcfaToken = "x", IsMain = i == 0 })
                    .ToList(),
            })));

    private async Task ClearMissionTablesAsync()
    {
        await using var db = fixture.CreateDbContext();
        await db.Database.ExecuteSqlRawAsync(
            """DELETE FROM "DailyMissions"; DELETE FROM "DailyMissionMemberCompletions";""");
    }

    private async Task SeedMissionAsync(DateOnly day, string type, string gameMode, int target, Guid? missionId = null, int fetchHourUtc = 0)
    {
        await using var db = fixture.CreateDbContext();
        db.Add(DailyMission.Create(
            missionId ?? Guid.NewGuid(),
            type,
            gameMode,
            currentProgress: 0,
            targetProgress: target,
            completed: false,
            endDate: new DateTimeOffset(day.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            rewardAmount: 100,
            rewardType: "Xp",
            fetchedAtUtc: new DateTimeOffset(day.ToDateTime(new TimeOnly(fetchHourUtc, 5)), TimeSpan.Zero)));
        await db.SaveChangesAsync();
    }

    private async Task SeedCompletionAsync(Guid clubId, string userId, DateOnly day, int completedCount)
    {
        await using var db = fixture.CreateDbContext();
        db.Add(DailyMissionMemberCompletion.Create(clubId, userId, day, completedCount));
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task StatsQuery_AggregatesMissionsAndCompletions_OverallAndPerClub()
    {
        await ClearMissionTablesAsync();

        var clubA = Guid.NewGuid();
        var clubB = Guid.NewGuid();
        var (userA1, userA2, userB1) = (NewUserId(), NewUserId(), NewUserId());
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var twoDaysAgo = today.AddDays(-2);
        var yesterday = today.AddDays(-1);

        await using (var db = fixture.CreateDbContext())
        {
            db.Add(Entities.Club.Create(clubA, $"club-{clubA:N}", level: 1));
            await db.SaveChangesAsync();
        }

        // Two days ago: a single mission, stored by two fetch batches (must dedupe).
        var duplicatedMissionId = Guid.NewGuid();
        await SeedMissionAsync(twoDaysAgo, "WinGames", "Duels", target: 3, duplicatedMissionId, fetchHourUtc: 0);
        await SeedMissionAsync(twoDaysAgo, "WinGames", "Duels", target: 3, duplicatedMissionId, fetchHourUtc: 12);
        // Yesterday: two missions on the same day.
        await SeedMissionAsync(yesterday, "WinGames", "Duels", target: 5);
        await SeedMissionAsync(yesterday, "Score", "Classic", target: 15000);

        // Club A snapshots for both days, club B only for the first day.
        await SeedCompletionAsync(clubA, userA1, twoDaysAgo, completedCount: 1);
        await SeedCompletionAsync(clubA, userA2, twoDaysAgo, completedCount: 0);
        await SeedCompletionAsync(clubA, userA1, yesterday, completedCount: 2);
        await SeedCompletionAsync(clubA, userA2, yesterday, completedCount: 0);
        await SeedCompletionAsync(clubB, userB1, twoDaysAgo, completedCount: 1);

        using var host = CreateHost(clubA, clubB);

        var allClubs = await host.SendAsync(new GetDailyMissionStatisticsQuery(null, 30));

        allClubs.IsSuccess.Should().BeTrue();
        var stats = allClubs.Value;
        stats.ClubName.Should().BeNull();
        stats.DaysWithMissionData.Should().Be(2);
        stats.TotalMissionAppearances.Should().Be(3);
        // Two days ago: 2 completions / (3 member rows × 1 mission); yesterday: 2 / (2 rows × 2 missions).
        stats.AverageDayCompletionRate.Should().BeApproximately((2.0 / 3.0 + 0.5) / 2.0, 1e-9);

        var winDuels = stats.Kinds.Single(k => k is { Type: "WinGames", GameMode: "Duels" });
        winDuels.AppearanceCount.Should().Be(2);
        winDuels.AppearanceDayShare.Should().BeApproximately(1.0, 1e-9);
        winDuels.AverageTargetProgress.Should().BeApproximately(4.0, 1e-9);
        winDuels.MinTargetProgress.Should().Be(3);
        winDuels.MaxTargetProgress.Should().Be(5);
        winDuels.LastAppearance.Should().Be(yesterday);
        winDuels.AverageDayCompletionRateWhenPresent.Should().BeApproximately((2.0 / 3.0 + 0.5) / 2.0, 1e-9);

        var scoreClassic = stats.Kinds.Single(k => k is { Type: "Score", GameMode: "Classic" });
        scoreClassic.AppearanceCount.Should().Be(1);
        scoreClassic.AppearanceDayShare.Should().BeApproximately(0.5, 1e-9);
        scoreClassic.AverageDayCompletionRateWhenPresent.Should().BeApproximately(0.5, 1e-9);

        var clubScoped = await host.SendAsync(new GetDailyMissionStatisticsQuery(clubA, 30));

        clubScoped.IsSuccess.Should().BeTrue();
        clubScoped.Value.ClubName.Should().Be($"club-{clubA:N}");
        // Club A alone: 1 / (2 rows × 1 mission) and 2 / (2 rows × 2 missions).
        clubScoped.Value.AverageDayCompletionRate.Should().BeApproximately(0.5, 1e-9);

        // The autocomplete feed: distinct kinds, deduplicated in SQL.
        var kinds = await host.SendAsync(new GetDailyMissionKindsQuery());
        kinds.Should().BeEquivalentTo([
            new DailyMissionKind("WinGames", "Duels"),
            new DailyMissionKind("Score", "Classic"),
        ]);
    }

    [Fact]
    public async Task StatsQuery_ReturnsNotFound_ForAnUnknownClub()
    {
        using var host = CreateHost(Guid.NewGuid());

        var result = await host.SendAsync(new GetDailyMissionStatisticsQuery(Guid.NewGuid(), 30));

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task SnapshotCommand_PersistsPerMemberCounts_AndReRunsIdempotently()
    {
        var clubId = Guid.NewGuid();
        var (userDone, userIdle) = (NewUserId(), NewUserId());
        var yesterday = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
        var yesterdayStartUtc = new DateTimeOffset(yesterday.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        await using (var db = fixture.CreateDbContext())
        {
            db.Add(Entities.Club.Create(clubId, $"club-{clubId:N}", level: 1));
            foreach (var userId in new[] { userDone, userIdle })
            {
                var user = GeoGuessrUser.Create(userId, $"nick-{userId[..16]}");
                db.Add(user);
                db.Add(ClubMember.Create(user, clubId, xp: 500, joinedAt: DateTimeOffset.UtcNow.AddMonths(-2)));
            }

            await db.SaveChangesAsync();
        }

        using var host = CreateHost(clubId);
        host.Mock<IGeoGuessrActivityReader>()
            .ReadActivitiesSinceAsync(clubId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns([
                new ReadClubActivitiesItemDto { UserId = userDone, XpReward = 20, RecordedAt = yesterdayStartUtc.AddHours(8) },
                new ReadClubActivitiesItemDto { UserId = userDone, XpReward = 20, RecordedAt = yesterdayStartUtc.AddHours(20) },
                // A regular game (wrong XP) and a completion outside the snapshotted day.
                new ReadClubActivitiesItemDto { UserId = userIdle, XpReward = 150, RecordedAt = yesterdayStartUtc.AddHours(9) },
                new ReadClubActivitiesItemDto { UserId = userIdle, XpReward = 20, RecordedAt = yesterdayStartUtc.AddHours(26) },
            ]);

        await host.SendAsync(new SnapshotDailyMissionCompletionsCommand());
        // A second run must detect the existing snapshot and not duplicate any rows.
        await host.SendAsync(new SnapshotDailyMissionCompletionsCommand());

        await using var read = fixture.CreateDbContext();
        var rows = await read.DailyMissionMemberCompletions.AsNoTracking()
            .Where(c => c.ClubId == clubId)
            .ToListAsync();

        rows.Should().HaveCount(2);
        rows.Should().OnlyContain(r => r.Date == yesterday);
        rows.Single(r => r.UserId == userDone).CompletedCount.Should().Be(2);
        rows.Single(r => r.UserId == userIdle).CompletedCount.Should().Be(0);
    }
}
