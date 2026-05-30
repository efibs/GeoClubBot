using Configuration;
using Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.UseCases.ClubMemberActivity;
using Xunit;

namespace GeoClubBot.Tests.Integration.UseCases;

/// <summary>
/// Reproduces the multi-club activity check the way <c>ActivityCheckJob</c> drives it: one
/// <see cref="CheckGeoGuessrPlayerActivityCommand"/> per configured club, fanned out concurrently
/// (each in its own DI scope / DbContext). Guards against the cross-club concurrency faults that
/// surface only when more than one club is configured.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class MultiClubActivityCheckUseCaseIntegrationTests(PostgresFixture fixture)
{
    private static string NewUserId() => Guid.NewGuid().ToString("N")[..24];
    private static string NewNickname() => $"nick-{Guid.NewGuid():N}"[..30];

    /// <summary>Host configured with several clubs (the first is the main one) and a per-check XP target of 500.</summary>
    private MediatorTestHost CreateHost(params Guid[] clubIds) =>
        new(fixture.ConnectionString, services =>
        {
            services.AddSingleton(Options.Create(new GeoGuessrConfiguration
            {
                SyncSchedule = "0 0 0 * * ?",
                ActivityNcfaToken = "x",
                MissionsNcfaToken = "x",
                UserProfileNcfaToken = "x",
                Clubs = clubIds
                    .Select((id, i) => new GeoGuessrClubEntry { ClubId = id, NcfaToken = "x", IsMain = i == 0 })
                    .ToList(),
            }));
            services.AddSingleton(Options.Create(new ActivityCheckerConfiguration
            {
                Schedule = "0 0 0 * * ?",
                TextChannelId = 1,
                MinXP = 500,
                GracePeriodDays = 0,
                MaxNumStrikes = 3,
                HistoryKeepTimeSpan = TimeSpan.FromDays(60),
                StrikeDecayTimeSpan = TimeSpan.FromDays(60),
            }));
        });

    private static ClubMemberDto BuildMemberDto(string userId, string nickname, int xp) => new()
    {
        User = new ClubMemberUserDto
        {
            UserId = userId,
            Nick = nickname,
            Avatar = "",
            FullBodyAvatar = "",
            BorderUrl = "",
            IsVerified = false,
            Flair = 0,
            CountryCode = "",
            TierId = 0,
            ClubUserType = 0,
        },
        Role = 0,
        JoinedAt = DateTimeOffset.UtcNow.AddMonths(-2),
        Xp = xp,
        WeeklyXp = 0,
    };

    private void ArrangeRoster(MediatorTestHost host, Guid clubId, params ClubMemberDto[] members)
    {
        var client = Substitute.For<IGeoGuessrClient>();
        client.ReadClubMembersAsync(clubId, Arg.Any<CancellationToken>()).Returns([.. members]);
        host.Mock<IGeoGuessrClientFactory>().CreateClient(clubId).Returns(client);
    }

    private async Task SeedClubAsync(Guid clubId)
    {
        await using var seed = fixture.CreateDbContext();
        seed.Add(Entities.Club.Create(clubId, $"club-{clubId:N}", level: 1));
        await seed.SaveChangesAsync();
    }

    private async Task SeedHistoryMemberAsync(Guid clubId, string userId, string nickname, DateTimeOffset lastCheck)
    {
        await using var seed = fixture.CreateDbContext();
        var user = GeoGuessrUser.Create(userId, nickname);
        seed.Add(user);
        seed.Add(ClubMember.Create(user, clubId, xp: 500, joinedAt: DateTimeOffset.UtcNow.AddMonths(-2)));
        seed.Add(ClubMemberHistoryEntry.Create(userId, clubId, xp: 500, lastCheck));
        await seed.SaveChangesAsync();
    }

    [Fact]
    public async Task ActivityCheck_AcrossTwoClubsConcurrently_StrikesEachClubsLaggingMember()
    {
        var clubA = Guid.NewGuid();
        var clubB = Guid.NewGuid();
        var lastCheck = DateTimeOffset.UtcNow.AddDays(-7);

        var activeA = (userId: NewUserId(), nickname: NewNickname());
        var laggingA = (userId: NewUserId(), nickname: NewNickname());
        var activeB = (userId: NewUserId(), nickname: NewNickname());
        var laggingB = (userId: NewUserId(), nickname: NewNickname());

        await SeedClubAsync(clubA);
        await SeedClubAsync(clubB);
        foreach (var (clubId, member) in new[] { (clubA, activeA), (clubA, laggingA), (clubB, activeB), (clubB, laggingB) })
        {
            await SeedHistoryMemberAsync(clubId, member.userId, member.nickname, lastCheck);
        }

        using var host = CreateHost(clubA, clubB);
        ArrangeRoster(host, clubA,
            BuildMemberDto(activeA.userId, activeA.nickname, xp: 1200),
            BuildMemberDto(laggingA.userId, laggingA.nickname, xp: 550));
        ArrangeRoster(host, clubB,
            BuildMemberDto(activeB.userId, activeB.nickname, xp: 1200),
            BuildMemberDto(laggingB.userId, laggingB.nickname, xp: 550));

        // Fan out exactly like ActivityCheckJob: one command per club, concurrently, each its own scope.
        var results = await Task.WhenAll(
            host.SendAsync(new CheckGeoGuessrPlayerActivityCommand(clubA)),
            host.SendAsync(new CheckGeoGuessrPlayerActivityCommand(clubB)));

        results.SelectMany(r => r).Should().HaveCount(4);

        await using var read = fixture.CreateDbContext();
        foreach (var lagging in new[] { laggingA, laggingB })
        {
            (await read.ClubMemberStrikes.AsNoTracking().CountAsync(s => s.UserId == lagging.userId && !s.Revoked))
                .Should().Be(1, "the lagging member in each club should receive exactly one strike");
        }
        foreach (var active in new[] { activeA, activeB })
        {
            (await read.ClubMemberStrikes.AsNoTracking().CountAsync(s => s.UserId == active.userId))
                .Should().Be(0, "the active member in each club should not be struck");
        }
    }

    [Fact]
    public async Task ActivityCheck_AcrossTwoClubsConcurrently_DecaysOldStrikesWithoutError()
    {
        // Strike decay (CheckStrikeDecay) deletes every strike older than the decay window across ALL
        // clubs — it is not partitioned per club. With multiple clubs the job fires it concurrently
        // from each branch, so two parallel DELETEs target the same global rows. This guards that
        // shared-row contention against deadlocks / errors and confirms the decayed strikes are gone.
        var clubA = Guid.NewGuid();
        var clubB = Guid.NewGuid();
        var lastCheck = DateTimeOffset.UtcNow.AddDays(-2);

        var memberA = (userId: NewUserId(), nickname: NewNickname());
        var memberB = (userId: NewUserId(), nickname: NewNickname());

        await SeedClubAsync(clubA);
        await SeedClubAsync(clubB);
        await SeedHistoryMemberAsync(clubA, memberA.userId, memberA.nickname, lastCheck);
        await SeedHistoryMemberAsync(clubB, memberB.userId, memberB.nickname, lastCheck);

        // An old strike for each member, well past the 1-day decay window configured below.
        await using (var seed = fixture.CreateDbContext())
        {
            seed.Add(ClubMemberStrike.Create(memberA.userId, DateTimeOffset.UtcNow.AddDays(-30)));
            seed.Add(ClubMemberStrike.Create(memberB.userId, DateTimeOffset.UtcNow.AddDays(-30)));
            await seed.SaveChangesAsync();
        }

        using var host = new MediatorTestHost(fixture.ConnectionString, services =>
        {
            services.AddSingleton(Options.Create(new GeoGuessrConfiguration
            {
                SyncSchedule = "0 0 0 * * ?",
                ActivityNcfaToken = "x",
                MissionsNcfaToken = "x",
                UserProfileNcfaToken = "x",
                Clubs =
                [
                    new GeoGuessrClubEntry { ClubId = clubA, NcfaToken = "x", IsMain = true },
                    new GeoGuessrClubEntry { ClubId = clubB, NcfaToken = "x", IsMain = false },
                ],
            }));
            services.AddSingleton(Options.Create(new ActivityCheckerConfiguration
            {
                Schedule = "0 0 0 * * ?",
                TextChannelId = 1,
                MinXP = 500,
                GracePeriodDays = 0,
                MaxNumStrikes = 3,
                HistoryKeepTimeSpan = TimeSpan.FromDays(60),
                StrikeDecayTimeSpan = TimeSpan.FromDays(1),
            }));
        });
        // Both members stay active (gain >= 500), so no NEW strike is created during the check.
        ArrangeRoster(host, clubA, BuildMemberDto(memberA.userId, memberA.nickname, xp: 1200));
        ArrangeRoster(host, clubB, BuildMemberDto(memberB.userId, memberB.nickname, xp: 1200));

        var act = async () => await Task.WhenAll(
            host.SendAsync(new CheckGeoGuessrPlayerActivityCommand(clubA)),
            host.SendAsync(new CheckGeoGuessrPlayerActivityCommand(clubB)));

        await act.Should().NotThrowAsync("concurrent strike decay across clubs must not deadlock or error");

        await using var read = fixture.CreateDbContext();
        var remaining = await read.ClubMemberStrikes.AsNoTracking()
            .CountAsync(s => s.UserId == memberA.userId || s.UserId == memberB.userId);
        remaining.Should().Be(0, "the decayed strikes should have been deleted");
    }
}
