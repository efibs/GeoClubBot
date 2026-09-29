using Entities;
using FluentAssertions;
using Infrastructure.OutputAdapters.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;
using DomainDailyMissionReminder = Entities.DailyMissionReminder;

namespace GeoClubBot.Tests.Integration;

/// <summary>
/// Integration tests for the EF repository methods that the plan flagged as high-value:
/// queries with hand-written predicates whose LINQ-to-SQL translation could silently
/// regress (correlated subqueries, group-by, payload filters). Each test seeds its own
/// data into a unique Club/UserId namespace so the shared container is reused safely.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class EfRepositoryIntegrationTests(PostgresFixture fixture)
{
    [Fact]
    public async Task ReadLatestHistoryEntryProjectionsByClubIdAsync_ReturnsLatestPerUserForGivenClub()
    {
        var clubId = Guid.NewGuid();
        var otherClubId = Guid.NewGuid();
        var user1 = Guid.NewGuid().ToString("N")[..24];
        var user2 = Guid.NewGuid().ToString("N")[..24];
        var user3 = Guid.NewGuid().ToString("N")[..24];

        await using (var seed = fixture.CreateDbContext())
        {
            seed.AddRange(
                Club.Create(clubId, $"club-{clubId:N}", 1),
                Club.Create(otherClubId, $"other-club-{otherClubId:N}", 1));

            foreach (var userId in new[] { user1, user2, user3 })
            {
                var user = GeoGuessrUser.Create(userId, $"nick-{userId}");
                seed.Add(user);
                seed.Add(ClubMember.Create(user, clubId, xp: 0, joinedAt: DateTimeOffset.UtcNow.AddMonths(-6)));
            }

            var t0 = DateTimeOffset.UtcNow.AddDays(-10);
            foreach (var userId in new[] { user1, user2, user3 })
            {
                seed.Add(ClubMemberHistoryEntry.Create(userId, clubId, xp: 100, t0));
                seed.Add(ClubMemberHistoryEntry.Create(userId, clubId, xp: 200, t0.AddDays(3)));
                seed.Add(ClubMemberHistoryEntry.Create(userId, clubId, xp: 300, t0.AddDays(7)));
            }

            // Same UserId with a later-timestamp entry in another club — the GroupBy(UserId)
            // must NOT confuse this for the latest entry in the queried club.
            seed.Add(ClubMemberHistoryEntry.Create(user1, otherClubId, xp: 999, t0.AddDays(9)));

            await seed.SaveChangesAsync();
        }

        await using var read = fixture.CreateDbContext();
        var repo = new EfHistoryRepository(read);

        var latest = await repo.ReadLatestHistoryEntryProjectionsByClubIdAsync(clubId);

        latest.Should().HaveCount(3);
        latest.Select(e => e.UserId).Should().BeEquivalentTo([user1, user2, user3]);
        latest.Should().OnlyContain(e => e.Xp == 300, "the latest of {100, 200, 300} is the 300 entry");
    }

    [Fact]
    public async Task ReadActiveStrikeCountsByMemberUserIdsAsync_ExcludesRevokedStrikes()
    {
        var clubId = Guid.NewGuid();
        var userA = Guid.NewGuid().ToString("N")[..24];
        var userB = Guid.NewGuid().ToString("N")[..24];
        var userC = Guid.NewGuid().ToString("N")[..24];

        await using (var seed = fixture.CreateDbContext())
        {
            seed.Add(Club.Create(clubId, $"club-{clubId:N}", 1));

            foreach (var userId in new[] { userA, userB, userC })
            {
                var user = GeoGuessrUser.Create(userId, $"nick-{userId}");
                seed.Add(user);
                seed.Add(ClubMember.Create(user, clubId, xp: 0, joinedAt: DateTimeOffset.UtcNow.AddMonths(-6)));
            }

            // User A: 2 active + 1 revoked → 2
            seed.Add(ClubMemberStrike.Create(userA, DateTimeOffset.UtcNow.AddDays(-3)));
            seed.Add(ClubMemberStrike.Create(userA, DateTimeOffset.UtcNow.AddDays(-2)));
            var revokedA = ClubMemberStrike.Create(userA, DateTimeOffset.UtcNow.AddDays(-1));
            revokedA.Revoke();
            seed.Add(revokedA);

            // User B: 0 active + 1 revoked → not in dict at all (GroupBy drops zero-rows)
            var revokedB = ClubMemberStrike.Create(userB, DateTimeOffset.UtcNow.AddDays(-1));
            revokedB.Revoke();
            seed.Add(revokedB);

            // User C: 1 active → 1
            seed.Add(ClubMemberStrike.Create(userC, DateTimeOffset.UtcNow.AddDays(-1)));

            await seed.SaveChangesAsync();
        }

        await using var read = fixture.CreateDbContext();
        var repo = new EfStrikesRepository(read);

        var counts = await repo.ReadActiveStrikeCountsByMemberUserIdsAsync([userA, userB, userC]);

        counts.Should().ContainKey(userA).WhoseValue.Should().Be(2);
        counts.Should().NotContainKey(userB, "the only B strike is revoked so its row drops out of the GroupBy result");
        counts.Should().ContainKey(userC).WhoseValue.Should().Be(1);
    }

    [Fact]
    public async Task ReadDueRemindersForUpdateAsync_AppliesTimeAndDateBoundaries()
    {
        var due = new TimeOnly(8, 0);
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var yesterday = today.AddDays(-1);

        // Mint base user IDs in the top of the ulong range so we don't collide with other tests.
        var baseUser = (ulong)Random.Shared.NextInt64(1_000_000_000_000_000L, long.MaxValue);
        var dueNeverSent = baseUser;
        var dueButSentToday = baseUser + 1;
        var dueAndSentYesterday = baseUser + 2;
        var wrongTime = baseUser + 3;

        await using (var seed = fixture.CreateDbContext())
        {
            seed.Add(DomainDailyMissionReminder.Create(dueNeverSent, due, null, null));

            var sentToday = DomainDailyMissionReminder.Create(dueButSentToday, due, null, null);
            sentToday.MarkSent(today);
            seed.Add(sentToday);

            var sentYesterday = DomainDailyMissionReminder.Create(dueAndSentYesterday, due, null, null);
            sentYesterday.MarkSent(yesterday);
            seed.Add(sentYesterday);

            seed.Add(DomainDailyMissionReminder.Create(wrongTime, due.AddHours(1), null, null));

            await seed.SaveChangesAsync();
        }

        await using var read = fixture.CreateDbContext();
        var repo = new EfDailyMissionReminderRepository(read);

        var found = await repo.ReadDueRemindersForUpdateAsync(due, today);

        var foundIds = found.Select(r => r.DiscordUserId).ToHashSet();
        foundIds.Should().Contain(dueNeverSent, "never-sent reminders at the due time are eligible");
        foundIds.Should().Contain(dueAndSentYesterday, "yesterday's sent flag should not block today's run");
        foundIds.Should().NotContain(dueButSentToday, "already-sent-today must be filtered out");
        foundIds.Should().NotContain(wrongTime, "reminders at a different time are not due");
    }

    [Fact]
    public async Task ReadMissedRemindersForUpdateAsync_ReturnsRemindersWhoseTimePassedTodayUnsent()
    {
        // Models a bot that went down at 19:55 and came back up at 20:05 the same day: everything
        // that should have fired by 20:05 but wasn't sent today is missed, exactly once.
        var rebootTime = new TimeOnly(20, 5);
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var yesterday = today.AddDays(-1);

        var baseUser = (ulong)Random.Shared.NextInt64(1_000_000_000_000_000L, long.MaxValue);
        var missedDuringDowntime = baseUser;
        var missedNeverSent = baseUser + 1;
        var missedForDays = baseUser + 2;
        var dueThisMinute = baseUser + 3;
        var alreadySentToday = baseUser + 4;
        var stillAheadToday = baseUser + 5;

        await using (var seed = fixture.CreateDbContext())
        {
            // 20:00 reminder that fired yesterday but was skipped today by the downtime.
            var duringDowntime = DomainDailyMissionReminder.Create(missedDuringDowntime, new TimeOnly(20, 0), null, null);
            duringDowntime.MarkSent(yesterday);
            seed.Add(duringDowntime);

            seed.Add(DomainDailyMissionReminder.Create(missedNeverSent, new TimeOnly(19, 0), null, null));

            // Offline for several days: still just one catch-up, for today.
            var forDays = DomainDailyMissionReminder.Create(missedForDays, new TimeOnly(8, 0), null, null);
            forDays.MarkSent(today.AddDays(-3));
            seed.Add(forDays);

            // Due at the very minute of the reboot — the boundary is inclusive.
            seed.Add(DomainDailyMissionReminder.Create(dueThisMinute, new TimeOnly(20, 5), null, null));

            var sentToday = DomainDailyMissionReminder.Create(alreadySentToday, new TimeOnly(20, 0), null, null);
            sentToday.MarkSent(today);
            seed.Add(sentToday);

            var ahead = DomainDailyMissionReminder.Create(stillAheadToday, new TimeOnly(22, 0), null, null);
            ahead.MarkSent(yesterday);
            seed.Add(ahead);

            await seed.SaveChangesAsync();
        }

        await using var read = fixture.CreateDbContext();
        var repo = new EfDailyMissionReminderRepository(read);

        var found = await repo.ReadMissedRemindersForUpdateAsync(rebootTime, today);

        var foundIds = found.Select(r => r.DiscordUserId).ToHashSet();
        foundIds.Should().Contain(missedDuringDowntime, "a 20:00 reminder skipped by 19:55–20:05 downtime must be caught up");
        foundIds.Should().Contain(missedNeverSent, "a never-sent reminder whose time passed today is missed");
        foundIds.Should().Contain(missedForDays, "multi-day downtime still yields exactly one catch-up (today's)");
        foundIds.Should().Contain(dueThisMinute, "a reminder due at the reboot minute itself is included");
        foundIds.Should().NotContain(alreadySentToday, "already-sent-today must not be re-sent");
        foundIds.Should().NotContain(stillAheadToday, "a reminder still ahead today will fire on its regular schedule");
    }

    [Fact]
    public async Task ReadClubMembersByUserIdsAsync_ReturnsOnlyRequestedMembers()
    {
        var clubId = Guid.NewGuid();
        var user1 = Guid.NewGuid().ToString("N")[..24];
        var user2 = Guid.NewGuid().ToString("N")[..24];
        var user3 = Guid.NewGuid().ToString("N")[..24];
        var unrelated = Guid.NewGuid().ToString("N")[..24];

        await using (var seed = fixture.CreateDbContext())
        {
            seed.Add(Club.Create(clubId, $"club-{clubId:N}", 1));

            foreach (var userId in new[] { user1, user2, user3, unrelated })
            {
                var user = GeoGuessrUser.Create(userId, $"nick-{userId}");
                seed.Add(user);
                seed.Add(ClubMember.Create(user, clubId, xp: 0, joinedAt: DateTimeOffset.UtcNow.AddMonths(-6)));
            }

            await seed.SaveChangesAsync();
        }

        await using var read = fixture.CreateDbContext();
        var repo = new EfClubMemberRepository(read);

        var result = await repo.ReadClubMembersByUserIdsAsync([user1, user2, user3]);

        result.Should().HaveCount(3);
        result.Keys.Should().BeEquivalentTo([user1, user2, user3]);
        result.Should().NotContainKey(unrelated, "members not in the input set must be excluded");
        result[user1].User.Nickname.Should().Be($"nick-{user1}", "the User navigation should be eagerly loaded");
    }

    [Fact]
    public async Task ReadClubMembersByUserIdsAsync_EmptyInputShortCircuits()
    {
        await using var read = fixture.CreateDbContext();
        var repo = new EfClubMemberRepository(read);

        var result = await repo.ReadClubMembersByUserIdsAsync([]);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task MissionBoardAlerts_ReadSentAlerts_ReturnsOnlyThisClubsRequestedMissions()
    {
        var clubId = Guid.NewGuid();
        var otherClubId = Guid.NewGuid();
        var alerted = Guid.NewGuid();
        var helpOnly = Guid.NewGuid();
        var notAsked = Guid.NewGuid();
        var sentAt = DateTimeOffset.UtcNow;

        await using (var seed = fixture.CreateDbContext())
        {
            var repo = new EfMissionBoardAlertRepository(seed);
            repo.Add(MissionBoardAlert.Create(clubId, alerted, MissionBoardAlertKind.OpenClaim, sentAt));
            repo.Add(MissionBoardAlert.Create(clubId, helpOnly, MissionBoardAlertKind.HelpRequest, sentAt));
            repo.Add(MissionBoardAlert.Create(clubId, notAsked, MissionBoardAlertKind.OpenClaim, sentAt));
            repo.Add(MissionBoardAlert.Create(otherClubId, alerted, MissionBoardAlertKind.HelpRequest, sentAt));
            await seed.SaveChangesAsync();
        }

        await using var read = fixture.CreateDbContext();
        var sent = await new EfMissionBoardAlertRepository(read).ReadSentAlertsAsync(clubId, [alerted, helpOnly]);

        sent.Should().BeEquivalentTo(new HashSet<(Guid, MissionBoardAlertKind)>
        {
            (alerted, MissionBoardAlertKind.OpenClaim),
            (helpOnly, MissionBoardAlertKind.HelpRequest)
        });
    }

    [Fact]
    public async Task MissionBoardAlerts_TheSameMissionAndKindCannotBeRecordedTwice()
    {
        var clubId = Guid.NewGuid();
        var missionId = Guid.NewGuid();

        await using (var seed = fixture.CreateDbContext())
        {
            seed.Add(MissionBoardAlert.Create(clubId, missionId, MissionBoardAlertKind.OpenClaim, DateTimeOffset.UtcNow));
            await seed.SaveChangesAsync();
        }

        await using var again = fixture.CreateDbContext();
        again.Add(MissionBoardAlert.Create(clubId, missionId, MissionBoardAlertKind.OpenClaim, DateTimeOffset.UtcNow));

        var act = () => again.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>("the unique index is what makes the alert idempotent");
    }

    [Fact]
    public async Task HistoryEntry_RoundTripsTheRecordedInterval()
    {
        var clubId = Guid.NewGuid();
        var userId = Guid.NewGuid().ToString("N")[..24];
        var timestamp = DateTimeOffset.UtcNow;

        await using (var seed = fixture.CreateDbContext())
        {
            seed.Add(Club.Create(clubId, $"club-{clubId:N}", 1));
            var user = GeoGuessrUser.Create(userId, $"nick-{userId}");
            seed.Add(user);
            seed.Add(ClubMember.Create(user, clubId, xp: 0, joinedAt: timestamp.AddMonths(-1)));
            var entry = ClubMemberHistoryEntry.Create(userId, clubId, 500, timestamp);
            entry.RecordInterval(ruleXp: 180, streakDays: 6, boardMissionCount: 4);
            seed.Add(entry);
            await seed.SaveChangesAsync();
        }

        await using var read = fixture.CreateDbContext();
        var projections = await new EfHistoryRepository(read).ReadHistoryEntryProjectionsByClubIdAsync(clubId);

        projections.Should().ContainSingle().Which.RuleXp.Should().Be(180);
        var stored = await read.ClubMemberHistoryEntries.SingleAsync(e => e.UserId == userId);
        stored.StreakDays.Should().Be(6);
        stored.BoardMissionCount.Should().Be(4);
    }
}
