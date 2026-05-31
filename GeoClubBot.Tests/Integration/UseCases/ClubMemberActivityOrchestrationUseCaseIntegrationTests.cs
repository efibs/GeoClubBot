using Configuration;
using Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using UseCases.OutputPorts.Discord;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.OutputPorts.Notifications;
using UseCases.OutputPorts.Rendering;
using UseCases.UseCases.ClubMemberActivity;
using Xunit;

namespace GeoClubBot.Tests.Integration.UseCases;

/// <summary>
/// Exercises the GeoGuessr-orchestration activity use cases end-to-end through the real MediatR
/// pipeline: <see cref="CheckGeoGuessrPlayerActivityCommand"/> (the multi-phase strike checker),
/// <see cref="ClubMemberActivityRewardCommand"/> (MVP announcement + role assignment) and
/// <see cref="RenderPlayerActivityQuery"/> (history chart). The GeoGuessr roster, Discord access and
/// chart renderer are substituted; strikes / history are asserted against Postgres.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class ClubMemberActivityOrchestrationUseCaseIntegrationTests(PostgresFixture fixture)
{
    private static string NewUserId() => Guid.NewGuid().ToString("N")[..24];
    private static string NewNickname() => $"nick-{Guid.NewGuid():N}"[..30];
    private static ulong NewDiscordId() => (ulong)Random.Shared.NextInt64(1_000_000_000_000_000L, long.MaxValue);

    /// <summary>Host whose main club is <paramref name="mainClubId"/> and whose default per-check XP target is 500.</summary>
    private MediatorTestHost CreateActivityHost(Guid mainClubId) =>
        new(fixture.ConnectionString, services =>
        {
            services.AddSingleton(Options.Create(new GeoGuessrConfiguration
            {
                SyncSchedule = "0 0 0 * * ?",
                ActivityNcfaToken = "x",
                MissionsNcfaToken = "x",
                UserProfileNcfaToken = "x",
                Clubs = [new GeoGuessrClubEntry { ClubId = mainClubId, NcfaToken = "x", IsMain = true }],
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
        client.ReadClubMembersAsync(clubId, Arg.Any<CancellationToken>())
            .Returns([.. members]);
        host.Mock<IGeoGuessrClientFactory>().CreateClient(clubId).Returns(client);
    }

    [Fact]
    public async Task CheckGeoGuessrPlayerActivity_StrikesTheMemberBelowTarget_AndSpareTheActiveOne()
    {
        var clubId = Guid.NewGuid();
        var active = (userId: NewUserId(), nickname: NewNickname());
        var inactive = (userId: NewUserId(), nickname: NewNickname());
        var lastCheck = DateTimeOffset.UtcNow.AddDays(-7);

        await using (var seed = fixture.CreateDbContext())
        {
            seed.Add(Entities.Club.Create(clubId, $"club-{clubId:N}", level: 1));
            foreach (var (userId, nickname) in new[] { active, inactive })
            {
                var user = GeoGuessrUser.Create(userId, nickname);
                seed.Add(user);
                seed.Add(ClubMember.Create(user, clubId, xp: 500, joinedAt: DateTimeOffset.UtcNow.AddMonths(-2)));
                seed.Add(ClubMemberHistoryEntry.Create(userId, clubId, xp: 500, lastCheck));
            }
            await seed.SaveChangesAsync();
        }

        using var host = CreateActivityHost(clubId);
        // Active gained 700 XP (>= 500 target); inactive only 100 XP (< 500 target → strike).
        ArrangeRoster(host, clubId,
            BuildMemberDto(active.userId, active.nickname, xp: 1200),
            BuildMemberDto(inactive.userId, inactive.nickname, xp: 600));

        var statuses = await host.SendAsync(new CheckGeoGuessrPlayerActivityCommand(clubId));

        statuses.Should().HaveCount(2);
        statuses.Single(s => s.UserId == active.userId).TargetAchieved.Should().BeTrue();
        var inactiveStatus = statuses.Single(s => s.UserId == inactive.userId);
        inactiveStatus.TargetAchieved.Should().BeFalse();
        inactiveStatus.NumStrikes.Should().Be(1);

        await using var read = fixture.CreateDbContext();
        var inactiveStrikes = await read.ClubMemberStrikes.AsNoTracking()
            .CountAsync(s => s.UserId == inactive.userId && !s.Revoked);
        inactiveStrikes.Should().Be(1);
        var activeStrikes = await read.ClubMemberStrikes.AsNoTracking()
            .CountAsync(s => s.UserId == active.userId);
        activeStrikes.Should().Be(0);

        // A fresh history snapshot is recorded for every member on each check.
        var inactiveHistoryCount = await read.ClubMemberHistoryEntries.AsNoTracking()
            .CountAsync(h => h.UserId == inactive.userId);
        inactiveHistoryCount.Should().Be(2);

        await host.Mock<IActivityStatusMessageSender>()
            .Received()
            .SendActivityStatusUpdateMessageAsync(
                Arg.Any<List<ClubMemberActivityStatus>>(), Arg.Any<string>(), 500, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ClubMemberActivityReward_AnnouncesTheMvp_AndAssignsTheRole()
    {
        var mvpDiscordId = NewDiscordId();
        var mvp = (userId: NewUserId(), nickname: NewNickname());
        var runnerUp = (userId: NewUserId(), nickname: NewNickname());

        await using (var seed = fixture.CreateDbContext())
        {
            seed.Add(GeoGuessrUser.Create(mvp.userId, mvp.nickname, mvpDiscordId));
            await seed.SaveChangesAsync();
        }

        using var host = new MediatorTestHost(fixture.ConnectionString);
        host.Mock<IDiscordServerRolesAccess>()
            .ReadMembersWithRoleAsync(Arg.Any<ulong>(), Arg.Any<CancellationToken>())
            .Returns([]);

        var statuses = new List<ClubMemberActivityStatus>
        {
            new(mvp.nickname, mvp.userId, true, XpSinceLastUpdate: 1000, 0, false, 500, null),
            new(runnerUp.nickname, runnerUp.userId, true, XpSinceLastUpdate: 400, 0, false, 500, null),
        };

        await host.SendAsync(new ClubMemberActivityRewardCommand(statuses));

        await host.Mock<IDiscordMessageAccess>()
            .Received()
            .SendMessageAsync(Arg.Is<string>(m => m.Contains("MVP")), Arg.Any<ulong>(), Arg.Any<CancellationToken>());
        await host.Mock<IDiscordServerRolesAccess>()
            .Received()
            .AddRoleToMembersByUserIdsAsync(
                Arg.Is<IEnumerable<ulong>>(ids => ids.Contains(mvpDiscordId)), Arg.Any<ulong>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ClubMemberActivityReward_DoesNotAnnounce_WhenNobodyGainedXp()
    {
        using var host = new MediatorTestHost(fixture.ConnectionString);
        host.Mock<IDiscordServerRolesAccess>()
            .ReadMembersWithRoleAsync(Arg.Any<ulong>(), Arg.Any<CancellationToken>())
            .Returns([]);

        var statuses = new List<ClubMemberActivityStatus>
        {
            new(NewNickname(), NewUserId(), false, XpSinceLastUpdate: 0, 1, false, 500, null),
        };

        await host.SendAsync(new ClubMemberActivityRewardCommand(statuses));

        await host.Mock<IDiscordMessageAccess>()
            .DidNotReceive()
            .SendMessageAsync(Arg.Any<string>(), Arg.Any<ulong>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RenderPlayerActivity_RendersTheChart_ForAMemberWithEnoughHistory()
    {
        var clubId = Guid.NewGuid();
        var userId = NewUserId();
        var nickname = NewNickname();

        await using (var seed = fixture.CreateDbContext())
        {
            seed.Add(Entities.Club.Create(clubId, $"club-{clubId:N}", level: 1));
            var user = GeoGuessrUser.Create(userId, nickname);
            seed.Add(user);
            seed.Add(ClubMember.Create(user, clubId, xp: 900, joinedAt: DateTimeOffset.UtcNow.AddMonths(-3)));
            seed.Add(ClubMemberHistoryEntry.Create(userId, clubId, xp: 300, DateTimeOffset.UtcNow.AddDays(-20)));
            seed.Add(ClubMemberHistoryEntry.Create(userId, clubId, xp: 600, DateTimeOffset.UtcNow.AddDays(-10)));
            seed.Add(ClubMemberHistoryEntry.Create(userId, clubId, xp: 900, DateTimeOffset.UtcNow.AddDays(-1)));
            await seed.SaveChangesAsync();
        }

        using var host = CreateActivityHost(clubId);
        using var rendered = new MemoryStream([1, 2, 3]);
        host.Mock<IHistoryRenderer>()
            .RenderHistory(Arg.Any<List<int>>(), Arg.Any<List<DateTimeOffset>>(), Arg.Any<int>())
            .Returns(rendered);

        var result = await host.SendAsync(new RenderPlayerActivityQuery(nickname, MaxNumHistoryEntries: 10, ClubName: null));

        result.Should().BeSameAs(rendered);
        host.Mock<IHistoryRenderer>()
            .Received()
            .RenderHistory(Arg.Any<List<int>>(), Arg.Any<List<DateTimeOffset>>(), Arg.Any<int>());
    }

    [Fact]
    public async Task RenderPlayerActivity_ReturnsNull_ForAnUnknownPlayer()
    {
        using var host = new MediatorTestHost(fixture.ConnectionString);

        var result = await host.SendAsync(
            new RenderPlayerActivityQuery($"missing-{Guid.NewGuid():N}"[..20], MaxNumHistoryEntries: 10, ClubName: null));

        result.Should().BeNull();
    }
}
