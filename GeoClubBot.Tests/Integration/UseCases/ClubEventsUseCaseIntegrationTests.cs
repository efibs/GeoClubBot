using Configuration;
using Entities;
using FluentAssertions;
using GeoClubBot.Tests.TestBuilders;
using GeoClubBot.Tests.TestDoubles;
using Infrastructure.OutputAdapters.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using UseCases.OutputPorts.Discord;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.OutputPorts.Notifications;
using UseCases.UseCases.Club;
using UseCases.UseCases.ClubEvents;
using Xunit;
using static GeoClubBot.Tests.TestBuilders.MissionBoards;

namespace GeoClubBot.Tests.Integration.UseCases;

/// <summary>
/// Batches of GeoGuessr socket events through the real pipeline: the stored memberships, the Discord
/// side effects their domain events fan out to (club role, private channel), and the claims and
/// levels pushed changes lead to.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class ClubEventsUseCaseIntegrationTests(PostgresFixture fixture)
{
    private const ulong RoleA = 6001;
    private const ulong RoleB = 6002;
    private const ulong ArchiveCategoryId = 43UL;

    private long _sequence;

    private static string NewUserId() => Guid.NewGuid().ToString("N")[..24];
    private static string NewNickname() => $"nick-{Guid.NewGuid():N}"[..30];
    private static ulong NewDiscordId() => (ulong)Random.Shared.NextInt64(1_000_000_000_000_000L, long.MaxValue);

    private PushOrder Next() => new(null, ++_sequence);

    private MediatorTestHost CreateHost(Guid clubA, Guid clubB) =>
        new(fixture.ConnectionString,
            services => services.AddSingleton(Options.Create(new GeoGuessrConfiguration
            {
                SyncSchedule = "0 0 0 * * ?",
                ActivityNcfaToken = "x",
                UserProfileNcfaToken = "x",
                Clubs =
                [
                    new GeoGuessrClubEntry { ClubId = clubA, NcfaToken = "x", IsMain = true, RoleId = RoleA },
                    new GeoGuessrClubEntry { ClubId = clubB, NcfaToken = "x", IsMain = false, RoleId = RoleB },
                ],
            })),
            configurationValues: new Dictionary<string, string?>
            {
                ["MemberPrivateChannels:Description"] = "Private channel",
                ["MemberPrivateChannels:ArchiveCategoryId"] = ArchiveCategoryId.ToString(),
                ["MemberPrivateChannels:ArchiveKeepTimeSpan"] = "30.00:00:00",
                ["MemberPrivateChannels:ArchiveCleanupSchedule"] = "0 0 3 * * ?",
                ["MissionClaimTracking:Schedule"] = "0 0 * ? * * *",
            });

    private static FakeDiscordChannels Discord(MediatorTestHost host)
    {
        var discord = (FakeDiscordChannels)host.Mock<IDiscordTextChannelAccess>();
        discord.AddCategory(ArchiveCategoryId, "archive");
        return discord;
    }

    private async Task SeedClubsAsync(Guid clubA, Guid clubB, int level = 1)
    {
        await using var seed = fixture.CreateDbContext();
        seed.Add(Entities.Club.Create(clubA, $"club-{clubA:N}", level));
        seed.Add(Entities.Club.Create(clubB, $"club-{clubB:N}", level));
        await seed.SaveChangesAsync();
    }

    /// <summary>A Discord-linked user, stored as a member of <paramref name="clubId"/> unless it is null.</summary>
    private async Task<(string UserId, string Nickname, ulong DiscordId)> SeedLinkedUserAsync(Guid? clubId)
    {
        var userId = NewUserId();
        var nickname = NewNickname();
        var discordId = NewDiscordId();

        await using var seed = fixture.CreateDbContext();
        var user = GeoGuessrUser.Create(userId, nickname, discordId);
        seed.Add(user);
        if (clubId is not null)
        {
            seed.Add(ClubMember.Create(user, clubId, xp: 100, joinedAt: DateTimeOffset.UtcNow.AddMonths(-3)));
        }

        await seed.SaveChangesAsync();
        return (userId, nickname, discordId);
    }

    private async Task<ClubMember?> ReadMemberAsync(string userId)
    {
        await using var read = fixture.CreateDbContext();
        return await new EfClubMemberRepository(read).ReadClubMemberByUserIdAsync(userId);
    }

    private ClubMemberJoinedPush Joined(Guid club, string userId, string nickname) =>
        new(club, Next(), userId, nickname, 0, DateTimeOffset.UtcNow);

    [Fact]
    public async Task APushedJoin_StoresTheMember_AddsTheClubRole_AndCreatesThePrivateChannel()
    {
        var (clubA, clubB) = (Guid.NewGuid(), Guid.NewGuid());
        await SeedClubsAsync(clubA, clubB);
        var (userId, nickname, discordId) = await SeedLinkedUserAsync(clubId: null);
        using var host = CreateHost(clubA, clubB);
        var discord = Discord(host);

        var complete = await host.SendAsync(new ApplyClubEventsCommand([Joined(clubA, userId, nickname)]));

        complete.Should().BeTrue();
        var member = await ReadMemberAsync(userId);
        member!.ClubId.Should().Be(clubA);
        member.PrivateTextChannelId.Should().NotBeNull();
        discord.Channels.Should().ContainKey(member.PrivateTextChannelId!.Value);
        await host.Mock<IDiscordServerRolesAccess>().Received().AddRoleToMembersByUserIdsAsync(
            Arg.Is<IEnumerable<ulong>>(ids => ids!.Contains(discordId)), RoleA, Arg.Any<CancellationToken>());
        // Nothing had to be read from GeoGuessr.
        host.Mock<IGeoGuessrClientFactory>().ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task APushedLeave_ClearsTheClub_RemovesTheRole_AndArchivesThePrivateChannel()
    {
        var (clubA, clubB) = (Guid.NewGuid(), Guid.NewGuid());
        await SeedClubsAsync(clubA, clubB);
        var (userId, nickname, discordId) = await SeedLinkedUserAsync(clubId: null);
        using var host = CreateHost(clubA, clubB);
        var discord = Discord(host);
        await host.SendAsync(new ApplyClubEventsCommand([Joined(clubA, userId, nickname)]));

        await host.SendAsync(new ApplyClubEventsCommand([new ClubMemberLeftPush(clubA, Next(), userId)]));

        var member = await ReadMemberAsync(userId);
        member!.ClubId.Should().BeNull();
        member.Xp.Should().Be(0, "a leave keeps the stored details");
        discord.Channels[member.PrivateTextChannelId!.Value].Parent.Should().Be(ArchiveCategoryId);
        await host.Mock<IDiscordServerRolesAccess>().Received().RemoveRolesFromUserAsync(
            discordId, Arg.Is<IEnumerable<ulong>>(roles => roles!.Contains(RoleA)), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ALeaveAndAJoinInOneBatch_AreOneSwitch_SoThePrivateChannelMovesInsteadOfBeingArchived()
    {
        var (clubA, clubB) = (Guid.NewGuid(), Guid.NewGuid());
        await SeedClubsAsync(clubA, clubB);
        var (userId, nickname, discordId) = await SeedLinkedUserAsync(clubId: null);
        using var host = CreateHost(clubA, clubB);
        var discord = Discord(host);
        await host.SendAsync(new ApplyClubEventsCommand([Joined(clubA, userId, nickname)]));
        var channelId = (await ReadMemberAsync(userId))!.PrivateTextChannelId!.Value;

        // The join on B's socket arrived before the leave on A's.
        await host.SendAsync(new ApplyClubEventsCommand(
            [Joined(clubB, userId, nickname), new ClubMemberLeftPush(clubA, Next(), userId)]));

        var member = await ReadMemberAsync(userId);
        member!.ClubId.Should().Be(clubB);
        member.PrivateTextChannelId.Should().Be(channelId);
        var categoryB = discord.CategoriesNamed($"club-{clubB:N} – Private").Should().ContainSingle().Subject;
        discord.Channels[channelId].Parent.Should().Be(categoryB.Id);
        await host.Mock<IDiscordServerRolesAccess>().Received().RemoveRolesFromUserAsync(
            discordId, Arg.Is<IEnumerable<ulong>>(roles => roles!.Contains(RoleA)), Arg.Any<CancellationToken>());
        await host.Mock<IDiscordServerRolesAccess>().Received().AddRoleToMembersByUserIdsAsync(
            Arg.Is<IEnumerable<ulong>>(ids => ids!.Contains(discordId)), RoleB, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ALeaveOfAClubTheMemberAlreadyLeft_ChangesNothing()
    {
        var (clubA, clubB) = (Guid.NewGuid(), Guid.NewGuid());
        await SeedClubsAsync(clubA, clubB);
        var (userId, _, _) = await SeedLinkedUserAsync(clubB);
        using var host = CreateHost(clubA, clubB);
        Discord(host);

        await host.SendAsync(new ApplyClubEventsCommand([new ClubMemberLeftPush(clubA, Next(), userId)]));

        (await ReadMemberAsync(userId))!.ClubId.Should().Be(clubB);
        host.Mock<IDiscordServerRolesAccess>().ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task APushedLevelUp_IsStoredAndAnnounced_AndBecomesTheBotStatusForTheMainClub()
    {
        var (clubA, clubB) = (Guid.NewGuid(), Guid.NewGuid());
        await SeedClubsAsync(clubA, clubB, level: 3);
        using var host = CreateHost(clubA, clubB);

        await host.SendAsync(new ApplyClubEventsCommand([new ClubUpdatedPush(clubA, Next(), 4, null)]));

        await using var read = fixture.CreateDbContext();
        (await read.Clubs.AsNoTracking().SingleAsync(c => c.ClubId == clubA)).Level.Should().Be(4);
        await host.Mock<IClubEventNotifier>().Received(1).SendClubLevelUpEvent(
            Arg.Is<Entities.Club>(c => c!.ClubId == clubA), Arg.Any<CancellationToken>());
        await host.Mock<IDiscordStatusUpdater>().Received().UpdateStatusAsync("Level 4 club!", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task APushedRename_IsStored()
    {
        var (clubA, clubB) = (Guid.NewGuid(), Guid.NewGuid());
        await SeedClubsAsync(clubA, clubB);
        using var host = CreateHost(clubA, clubB);

        await host.SendAsync(new ApplyClubEventsCommand([new ClubUpdatedPush(clubB, Next(), null, "Renamed club")]));

        await using var read = fixture.CreateDbContext();
        (await read.Clubs.AsNoTracking().SingleAsync(c => c.ClubId == clubB)).Name.Should().Be("Renamed club");
    }

    [Fact]
    public async Task ABoardUpdate_RecordsOnlyThatClubsClaims()
    {
        var (clubA, clubB) = (Guid.NewGuid(), Guid.NewGuid());
        await SeedClubsAsync(clubA, clubB);
        using var host = CreateHost(clubA, clubB);
        var boards = host.Mock<IClubMissionBoardReader>();
        boards.ReadCurrentAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Week(Board(1, Tile(claimedBy: "claimer", claimedAt: PeriodStart.AddHours(1)))));

        await host.SendAsync(new ApplyClubEventsCommand([new ClubMissionBoardUpdatedPush(clubB, Next())]));

        await using var read = fixture.CreateDbContext();
        var claims = await read.MissionClaims.AsNoTracking().Where(c => c.ClubId == clubA || c.ClubId == clubB).ToListAsync();
        claims.Should().ContainSingle().Which.ClubId.Should().Be(clubB);
        host.Mock<IGeoGuessrCacheInvalidator>().Received(1).InvalidateCurrentMissionBoard(clubB);
    }

    [Fact]
    public async Task APushedJoin_RacingTheScheduledSync_StoresTheMemberOnce_AndJoinsThemOnce()
    {
        var (clubA, clubB) = (Guid.NewGuid(), Guid.NewGuid());
        await SeedClubsAsync(clubA, clubB);
        var (userId, nickname, discordId) = await SeedLinkedUserAsync(clubId: null);
        using var host = CreateHost(clubA, clubB);
        Discord(host);

        // The scheduled sync reads A with the new member in it, and B empty.
        var memberDto = GeoGuessrDtos.Member(userId, nickname, xp: 0);
        foreach (var (club, members) in new[] { (clubA, new List<ClubMemberDto> { memberDto }), (clubB, []) })
        {
            var dto = GeoGuessrDtos.Club(club, name: $"club-{club:N}");
            dto.Members = members;
            var client = Substitute.For<IGeoGuessrClient>();
            client.ReadClubAsync(club, Arg.Any<CancellationToken>()).Returns(dto);
            host.Mock<IGeoGuessrClientFactory>().CreateClient(club).Returns(client);
        }

        await Task.WhenAll(
            host.SendAsync(new SyncClubsCommand()),
            host.SendAsync(new ApplyClubEventsCommand([Joined(clubA, userId, nickname)])));

        await using var read = fixture.CreateDbContext();
        (await read.ClubMembers.AsNoTracking().CountAsync(m => m.UserId == userId)).Should().Be(1);
        await host.Mock<IDiscordServerRolesAccess>().Received(1).AddRoleToMembersByUserIdsAsync(
            Arg.Is<IEnumerable<ulong>>(ids => ids!.Contains(discordId)), RoleA, Arg.Any<CancellationToken>());
    }
}
