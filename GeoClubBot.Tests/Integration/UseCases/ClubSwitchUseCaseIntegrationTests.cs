using Configuration;
using Entities;
using FluentAssertions;
using GeoClubBot.Tests.TestDoubles;
using Infrastructure.OutputAdapters.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using UseCases.OutputPorts.Discord;
using UseCases.UseCases.ClubMembers;
using Xunit;

namespace GeoClubBot.Tests.Integration.UseCases;

/// <summary>
/// Exercises the club-membership transitions that historically caused trouble — a member joining a
/// club (from unsynced / no club), leaving a club, and switching between clubs — through the real
/// MediatR pipeline via <see cref="SaveClubMembersCommand"/>. Each transition fires a domain event
/// (PlayerJoined / PlayerLeft / PlayerSwitchedClubs) that fans out to the role and private-channel
/// notification handlers; the tests assert both the persisted membership and those Discord side effects.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class ClubSwitchUseCaseIntegrationTests(PostgresFixture fixture)
{
    private const ulong RoleA = 5001;
    private const ulong RoleB = 5002;

    private static string NewUserId() => Guid.NewGuid().ToString("N")[..24];
    private static string NewNickname() => $"nick-{Guid.NewGuid():N}"[..30];
    private static ulong NewDiscordId() => (ulong)Random.Shared.NextInt64(1_000_000_000_000_000L, long.MaxValue);

    /// <summary>Host configured with two clubs (A is main), each with its own member role, plus private-channel config.</summary>
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
                ["MemberPrivateChannels:ArchiveCategoryId"] = "43",
                ["MemberPrivateChannels:ArchiveKeepTimeSpan"] = "30.00:00:00",
                ["MemberPrivateChannels:ArchiveCleanupSchedule"] = "0 0 3 * * ?",
            });

    private const ulong LegacyCategoryId = 42UL;
    private const ulong ArchiveCategoryId = 43UL;

    /// <summary>The host's in-memory Discord server, with the archive and an old shared category.</summary>
    private static FakeDiscordChannels Discord(MediatorTestHost host)
    {
        var discord = (FakeDiscordChannels)host.Mock<IDiscordTextChannelAccess>();
        discord.AddCategory(LegacyCategoryId, "private channels");
        discord.AddCategory(ArchiveCategoryId, "archive");
        return discord;
    }

    private async Task SeedClubsAsync(params Guid[] clubIds)
    {
        await using var seed = fixture.CreateDbContext();
        foreach (var clubId in clubIds)
        {
            seed.Add(Entities.Club.Create(clubId, $"club-{clubId:N}", level: 1));
        }
        await seed.SaveChangesAsync();
    }

    /// <summary>Seeds a Discord-linked GeoGuessr user and, optionally, an existing club membership.</summary>
    private async Task<(string userId, string nickname, ulong discordId)> SeedLinkedMemberAsync(
        Guid? clubId, ulong discordId, ulong? privateChannelId = null, bool withMember = false,
        DateTimeOffset? privateChannelArchivedAt = null)
    {
        var userId = NewUserId();
        var nickname = NewNickname();

        await using var seed = fixture.CreateDbContext();
        var user = GeoGuessrUser.Create(userId, nickname, discordId);
        seed.Add(user);

        if (clubId is not null || privateChannelId is not null || withMember)
        {
            var member = ClubMember.Create(user, clubId, xp: 100, joinedAt: DateTimeOffset.UtcNow.AddMonths(-3));
            if (privateChannelId is not null)
            {
                member.SetPrivateTextChannelId(privateChannelId.Value);

                if (privateChannelArchivedAt is not null)
                {
                    member.ArchivePrivateTextChannel(privateChannelArchivedAt.Value);
                }
            }
            seed.Add(member);
        }

        await seed.SaveChangesAsync();
        return (userId, nickname, discordId);
    }

    [Fact]
    public async Task SaveClubMembers_BrandNewUnsyncedMemberJoinsClub_AddsRoleAndCreatesPrivateChannel()
    {
        var clubA = Guid.NewGuid();
        var clubB = Guid.NewGuid();
        await SeedClubsAsync(clubA, clubB);

        // Linked Discord user that has never been synced as a club member yet.
        var (userId, nickname, discordId) = await SeedLinkedMemberAsync(clubId: null, discordId: NewDiscordId());

        using var host = CreateHost(clubA, clubB);
        var discord = Discord(host);
        var snapshot = new ClubMemberSyncSnapshot(userId, nickname, clubA, 100, DateTimeOffset.UtcNow.AddMonths(-3));

        await host.SendAsync(new SaveClubMembersCommand([snapshot]));

        await using var read = fixture.CreateDbContext();
        var member = await new EfClubMemberRepository(read).ReadClubMemberByUserIdAsync(userId);
        member!.ClubId.Should().Be(clubA);

        await host.Mock<IDiscordServerRolesAccess>()
            .Received()
            .AddRoleToMembersByUserIdsAsync(
                Arg.Is<IEnumerable<ulong>>(ids => ids!.Contains(discordId)), RoleA, Arg.Any<CancellationToken>());
        var category = discord.CategoriesNamed($"club-{clubA:N} – Private").Should().ContainSingle().Subject;
        member.PrivateTextChannelId.Should().NotBeNull();
        discord.Channels[member.PrivateTextChannelId!.Value].Parent.Should().Be(category.Id);
    }

    [Fact]
    public async Task SaveClubMembers_ExistingMemberWithNoClubJoinsClub_AddsRole()
    {
        var clubA = Guid.NewGuid();
        var clubB = Guid.NewGuid();
        await SeedClubsAsync(clubA, clubB);

        // Member row already exists but with no club (ClubId == null).
        var (userId, nickname, discordId) = await SeedLinkedMemberAsync(clubId: null, discordId: NewDiscordId(), withMember: true);

        using var host = CreateHost(clubA, clubB);
        var snapshot = new ClubMemberSyncSnapshot(userId, nickname, clubB, 100, DateTimeOffset.UtcNow.AddMonths(-3));

        await host.SendAsync(new SaveClubMembersCommand([snapshot]));

        await using var read = fixture.CreateDbContext();
        var member = await new EfClubMemberRepository(read).ReadClubMemberByUserIdAsync(userId);
        member!.ClubId.Should().Be(clubB);

        await host.Mock<IDiscordServerRolesAccess>()
            .Received()
            .AddRoleToMembersByUserIdsAsync(
                Arg.Is<IEnumerable<ulong>>(ids => ids!.Contains(discordId)), RoleB, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveClubMembers_MemberLeavesClub_RemovesRoleAndArchivesPrivateChannel()
    {
        var clubA = Guid.NewGuid();
        var clubB = Guid.NewGuid();
        await SeedClubsAsync(clubA, clubB);

        const ulong channelId = 999UL;
        var (userId, nickname, discordId) = await SeedLinkedMemberAsync(clubId: clubA, discordId: NewDiscordId(), privateChannelId: channelId);

        using var host = CreateHost(clubA, clubB);
        var discord = Discord(host);
        discord.AddChannel(channelId, LegacyCategoryId);

        // A null TargetClubId is exactly what SyncClubs emits for a member no longer in any roster.
        var snapshot = new ClubMemberSyncSnapshot(userId, nickname, null, 100, DateTimeOffset.UtcNow.AddMonths(-3));

        await host.SendAsync(new SaveClubMembersCommand([snapshot]));

        await using var read = fixture.CreateDbContext();
        var member = await new EfClubMemberRepository(read).ReadClubMemberByUserIdAsync(userId);
        member!.ClubId.Should().BeNull();
        member.PrivateTextChannelId.Should().Be(channelId, "leaving archives the channel rather than deleting it");
        member.PrivateTextChannelArchivedAt.Should().NotBeNull();

        await host.Mock<IDiscordServerRolesAccess>()
            .Received()
            .RemoveRolesFromUserAsync(
                discordId, Arg.Is<IEnumerable<ulong>>(roles => roles!.Contains(RoleA)), Arg.Any<CancellationToken>());
        discord.Channels[channelId].Parent.Should().Be(ArchiveCategoryId);
        discord.Writes.Should().NotContain(w => w.StartsWith("delete"));
    }

    [Fact]
    public async Task SaveClubMembers_MemberWithArchivedChannelRejoins_RestoresTheSameChannel()
    {
        var clubA = Guid.NewGuid();
        var clubB = Guid.NewGuid();
        await SeedClubsAsync(clubA, clubB);

        // The shape a hop to the second club leaves behind: out of every club, channel archived.
        const ulong channelId = 777UL;
        var (userId, nickname, _) = await SeedLinkedMemberAsync(clubId: null, discordId: NewDiscordId(),
            privateChannelId: channelId, privateChannelArchivedAt: DateTimeOffset.UtcNow.AddMinutes(-1));

        using var host = CreateHost(clubA, clubB);
        var discord = Discord(host);
        discord.AddChannel(channelId, ArchiveCategoryId);

        var snapshot = new ClubMemberSyncSnapshot(userId, nickname, clubB, 100, DateTimeOffset.UtcNow.AddMonths(-3));

        await host.SendAsync(new SaveClubMembersCommand([snapshot]));

        await using var read = fixture.CreateDbContext();
        var member = await new EfClubMemberRepository(read).ReadClubMemberByUserIdAsync(userId);
        member!.ClubId.Should().Be(clubB);
        member.PrivateTextChannelId.Should().Be(channelId);
        member.PrivateTextChannelArchivedAt.Should().BeNull("rejoining takes the channel out of the archive");

        var clubBCategory = discord.CategoriesNamed($"club-{clubB:N} – Private").Should().ContainSingle().Subject;
        discord.Channels[channelId].Parent.Should().Be(clubBCategory.Id, "it is restored into the new club's category");
        discord.Writes.Should().NotContain(w => w.StartsWith("create-channel"));
    }

    [Fact]
    public async Task SaveClubMembers_MemberSwitchesClubs_SwapsRolesAndMovesThePrivateChannelToTheNewClub()
    {
        var clubA = Guid.NewGuid();
        var clubB = Guid.NewGuid();
        await SeedClubsAsync(clubA, clubB);

        const ulong channelId = 888UL;
        var (userId, nickname, discordId) = await SeedLinkedMemberAsync(clubId: clubA, discordId: NewDiscordId(), privateChannelId: channelId);

        using var host = CreateHost(clubA, clubB);
        var discord = Discord(host);
        discord.AddChannel(channelId, LegacyCategoryId);
        var snapshot = new ClubMemberSyncSnapshot(userId, nickname, clubB, 100, DateTimeOffset.UtcNow.AddMonths(-3));

        await host.SendAsync(new SaveClubMembersCommand([snapshot]));

        await using var read = fixture.CreateDbContext();
        var member = await new EfClubMemberRepository(read).ReadClubMemberByUserIdAsync(userId);
        member!.ClubId.Should().Be(clubB);
        member.PrivateTextChannelId.Should().Be(channelId, "switching clubs must not remove the private channel");

        var roles = host.Mock<IDiscordServerRolesAccess>();
        await roles.Received()
            .RemoveRolesFromUserAsync(
                discordId, Arg.Is<IEnumerable<ulong>>(r => r!.Contains(RoleA)), Arg.Any<CancellationToken>());
        await roles.Received()
            .AddRoleToMembersByUserIdsAsync(
                Arg.Is<IEnumerable<ulong>>(ids => ids!.Contains(discordId)), RoleB, Arg.Any<CancellationToken>());

        // The channel follows the member into the new club's category; nothing is created or deleted.
        var clubBCategory = discord.CategoriesNamed($"club-{clubB:N} – Private").Should().ContainSingle().Subject;
        discord.Channels[channelId].Parent.Should().Be(clubBCategory.Id);
        discord.Writes.Should().NotContain(w => w.StartsWith("delete") || w.StartsWith("create-channel"));
    }
}
