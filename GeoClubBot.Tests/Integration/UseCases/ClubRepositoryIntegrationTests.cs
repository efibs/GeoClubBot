using Entities;
using FluentAssertions;
using Infrastructure.OutputAdapters.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GeoClubBot.Tests.Integration.UseCases;

/// <summary>
/// Integration tests for <see cref="EfClubRepository"/> against the shared Postgres container. Guards
/// that a club sync (<c>CreateOrUpdateClubAsync</c>) refreshes only the API-sourced fields and does not
/// wipe server-owned columns such as <c>LatestActivityCheckTime</c> — the regression behind issue #200,
/// where the previous blind <c>Update(club)</c> reset the check time to null on every sync.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class ClubRepositoryIntegrationTests(PostgresFixture fixture)
{
    [Fact]
    public async Task CreateOrUpdateClub_PreservesLatestActivityCheckTime_WhileRefreshingNameAndLevel()
    {
        var clubId = Guid.NewGuid();
        var checkTime = DateTimeOffset.UtcNow.AddDays(-3);

        await using (var seed = fixture.CreateDbContext())
        {
            seed.Add(Club.Create(clubId, "Old Name", level: 1, latestActivityCheckTime: checkTime));
            await seed.SaveChangesAsync();
        }

        // Sync payload carries a fresh club with a null check time (as ClubAssembler builds it).
        await using (var act = fixture.CreateDbContext())
        {
            await new EfClubRepository(act).CreateOrUpdateClubAsync(Club.Create(clubId, "New Name", level: 5));
            await act.SaveChangesAsync();
        }

        await using var read = fixture.CreateDbContext();
        var club = await read.Clubs.AsNoTracking().SingleAsync(c => c.ClubId == clubId);
        club.Name.Should().Be("New Name");
        club.Level.Should().Be(5);
        club.LatestActivityCheckTime.Should().NotBeNull();
        club.LatestActivityCheckTime!.Value.Should().BeCloseTo(checkTime, TimeSpan.FromSeconds(1),
            "a club sync must not reset the recorded activity-check time");
    }

    [Fact]
    public async Task CreateOrUpdateClub_InsertsANewClub_WhenNoneExists()
    {
        var clubId = Guid.NewGuid();

        await using (var act = fixture.CreateDbContext())
        {
            await new EfClubRepository(act).CreateOrUpdateClubAsync(Club.Create(clubId, "Brand New", level: 2));
            await act.SaveChangesAsync();
        }

        await using var read = fixture.CreateDbContext();
        var club = await read.Clubs.AsNoTracking().SingleAsync(c => c.ClubId == clubId);
        club.Name.Should().Be("Brand New");
        club.Level.Should().Be(2);
        club.LatestActivityCheckTime.Should().BeNull();
    }

    [Fact]
    public async Task BackfillMigrationSql_SetsNullCheckTimesToTheMostRecentHistoryTimestamp()
    {
        // Verifies the data effect of migration 20260709204652_BackfillClubLatestActivityCheckTime
        // against real Postgres. Keep this SQL in sync with that migration's Up().
        var clubId = Guid.NewGuid();
        var maxTimestamp = DateTimeOffset.UtcNow.AddDays(-1);
        var userId = Guid.NewGuid().ToString("N")[..24];

        await using (var seed = fixture.CreateDbContext())
        {
            seed.Add(Club.Create(clubId, $"club-{clubId:N}", level: 1)); // never checked -> null
            var user = GeoGuessrUser.Create(userId, $"nick-{Guid.NewGuid():N}"[..30]);
            seed.Add(user);
            seed.Add(ClubMember.Create(user, clubId, xp: 100, joinedAt: DateTimeOffset.UtcNow.AddMonths(-2)));
            seed.Add(ClubMemberHistoryEntry.Create(userId, clubId, xp: 50, maxTimestamp.AddDays(-5)));
            seed.Add(ClubMemberHistoryEntry.Create(userId, clubId, xp: 100, maxTimestamp));
            await seed.SaveChangesAsync();
        }

        await using (var act = fixture.CreateDbContext())
        {
            await act.Database.ExecuteSqlRawAsync(
                """
                UPDATE "Clubs" AS c
                SET "LatestActivityCheckTime" = h.max_timestamp
                FROM (
                    SELECT "ClubId", MAX("Timestamp") AS max_timestamp
                    FROM "ClubMemberHistoryEntries"
                    GROUP BY "ClubId"
                ) AS h
                WHERE c."ClubId" = h."ClubId"
                  AND c."LatestActivityCheckTime" IS NULL;
                """);
        }

        await using var read = fixture.CreateDbContext();
        var club = await read.Clubs.AsNoTracking().SingleAsync(c => c.ClubId == clubId);
        club.LatestActivityCheckTime.Should().NotBeNull();
        club.LatestActivityCheckTime!.Value.Should().BeCloseTo(maxTimestamp, TimeSpan.FromSeconds(1),
            "the backfill copies the newest history timestamp into a never-checked club");
    }
}
