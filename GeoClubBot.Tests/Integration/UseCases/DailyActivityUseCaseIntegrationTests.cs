using Configuration;
using Entities;
using FluentAssertions;
using GeoClubBot.Tests.TestBuilders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.UseCases.DailyActivity;
using Xunit;

namespace GeoClubBot.Tests.Integration.UseCases;

/// <summary>
/// Exercises the daily activity snapshot command and the streak query end-to-end through the real
/// MediatR pipeline against Postgres. Each test namespaces its rows by a fresh club id.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class DailyActivityUseCaseIntegrationTests(PostgresFixture fixture)
{
    private static string NewUserId() => Guid.NewGuid().ToString("N")[..24];

    private MediatorTestHost CreateHost(params Guid[] clubIds) =>
        new(fixture.ConnectionString, services =>
            services.AddSingleton(Options.Create(new GeoGuessrConfiguration
            {
                SyncSchedule = "0 0 0 * * ?",
                ActivityNcfaToken = "x",
                UserProfileNcfaToken = "x",
                Clubs = clubIds
                    .Select((id, i) => new GeoGuessrClubEntry { ClubId = id, NcfaToken = "x", IsMain = i == 0 })
                    .ToList(),
            })));

    private async Task SeedClubAsync(Guid clubId, params string[] userIds)
    {
        await using var db = fixture.CreateDbContext();
        db.Add(Entities.Club.Create(clubId, $"club-{clubId:N}", level: 1));
        foreach (var userId in userIds)
        {
            var user = GeoGuessrUser.Create(userId, $"nick-{userId[..16]}");
            db.Add(user);
            db.Add(ClubMember.Create(user, clubId, xp: 500, joinedAt: DateTimeOffset.UtcNow.AddMonths(-2)));
        }

        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task SnapshotCommand_PersistsPerMemberCounts_AndReRunsIdempotently()
    {
        var clubId = Guid.NewGuid();
        var (userDone, userIdle) = (NewUserId(), NewUserId());
        var yesterday = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
        var yesterdayStartUtc = new DateTimeOffset(yesterday.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        await SeedClubAsync(clubId, userDone, userIdle);

        using var host = CreateHost(clubId);
        host.Mock<IGeoGuessrActivityReader>()
            .ReadActivitiesSinceAsync(clubId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns([
                // Both worth 20 XP, and counted apart.
                ClubActivities.Challenge(userDone, yesterdayStartUtc.AddHours(8)),
                ClubActivities.BoardMission(userDone, yesterdayStartUtc.AddHours(20)),
                ClubActivities.BoardBonus(userDone, yesterdayStartUtc.AddHours(20)),
                // An entry outside the snapshotted day.
                ClubActivities.Challenge(userIdle, yesterdayStartUtc.AddHours(26)),
            ]);

        await host.SendAsync(new SnapshotDailyActivityCommand());
        // A second run must detect the existing snapshot and not duplicate any rows.
        await host.SendAsync(new SnapshotDailyActivityCommand());

        await using var read = fixture.CreateDbContext();
        var rows = await read.ClubMemberDailyActivities.AsNoTracking()
            .Where(c => c.ClubId == clubId)
            .ToListAsync();

        rows.Should().HaveCount(2);
        rows.Should().OnlyContain(r => r.Date == yesterday);
        var doneRow = rows.Single(r => r.UserId == userDone);
        doneRow.DailyChallengeCount.Should().Be(1);
        doneRow.BoardMissionCount.Should().Be(1);
        doneRow.BoardClearBonusCount.Should().Be(1);
        doneRow.Xp.Should().Be(140);

        var idleRow = rows.Single(r => r.UserId == userIdle);
        idleRow.DailyChallengeCount.Should().Be(0);
        idleRow.Xp.Should().Be(0);
    }

    [Fact]
    public async Task StreakQuery_ReadsTheSnapshots_IncludingRowsFromBeforeTheBoard()
    {
        var clubId = Guid.NewGuid();
        var userId = NewUserId();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await SeedClubAsync(clubId, userId);

        await using (var db = fixture.CreateDbContext())
        {
            db.Add(ClubMemberDailyActivity.Create(clubId, userId, today.AddDays(-1), dailyChallengeCount: 1, boardMissionCount: 1));
            db.Add(ClubMemberDailyActivity.Create(clubId, userId, today.AddDays(-2), dailyChallengeCount: 1));
            // Written before the challenge was tracked: judged on the old daily mission.
            db.Add(ClubMemberDailyActivity.Create(clubId, userId, today.AddDays(-3), dailyChallengeCount: null, legacyDailyMissionCount: 1));
            db.Add(ClubMemberDailyActivity.Create(clubId, userId, today.AddDays(-4), dailyChallengeCount: 0));
            await db.SaveChangesAsync();
        }

        using var host = CreateHost(clubId);
        var streaks = await host.SendAsync(new GetDailyStreaksQuery(clubId, 30));

        streaks.Should().ContainSingle();
        streaks[0].CurrentStreak.Should().Be(3);
        streaks[0].LongestStreak.Should().Be(3);
    }
}
