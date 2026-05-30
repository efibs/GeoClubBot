using Configuration;
using Entities;
using FluentAssertions;
using Infrastructure.OutputAdapters;
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
                MissionsNcfaToken = "x",
                UserProfileNcfaToken = "x",
                Clubs =
                [
                    new GeoGuessrClubEntry { ClubId = clubA, NcfaToken = "x", IsMain = true, RoleId = RoleA },
                    new GeoGuessrClubEntry { ClubId = clubB, NcfaToken = "x", IsMain = false, RoleId = RoleB },
                ],
            })),
            configurationValues: new Dictionary<string, string?>
            {
                ["MemberPrivateChannels:CategoryId"] = "42",
                ["MemberPrivateChannels:Description"] = "Private channel",
            });

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
        Guid? clubId, ulong discordId, ulong? privateChannelId = null, bool withMember = false)
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
        var snapshot = new ClubMemberSyncSnapshot(userId, nickname, clubA, 100, DateTimeOffset.UtcNow.AddMonths(-3));

        await host.SendAsync(new SaveClubMembersCommand([snapshot]));

        await using var read = fixture.CreateDbContext();
        var member = await new EfClubMemberRepository(read).ReadClubMemberByUserIdAsync(userId);
        member!.ClubId.Should().Be(clubA);

        await host.Mock<IDiscordServerRolesAccess>()
            .Received()
            .AddRoleToMembersByUserIdsAsync(
                Arg.Is<IEnumerable<ulong>>(ids => ids.Contains(discordId)), RoleA, Arg.Any<CancellationToken>());
        await host.Mock<IDiscordTextChannelAccess>()
            .Received()
            .CreatePrivateTextChannelAsync(
                Arg.Any<ulong>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<IEnumerable<ulong>?>(), Arg.Any<IEnumerable<ulong>?>(), Arg.Any<CancellationToken>());
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
                Arg.Is<IEnumerable<ulong>>(ids => ids.Contains(discordId)), RoleB, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveClubMembers_MemberLeavesClub_RemovesRoleAndDeletesPrivateChannel()
    {
        var clubA = Guid.NewGuid();
        var clubB = Guid.NewGuid();
        await SeedClubsAsync(clubA, clubB);

        const ulong channelId = 999UL;
        var (userId, nickname, discordId) = await SeedLinkedMemberAsync(clubId: clubA, discordId: NewDiscordId(), privateChannelId: channelId);

        using var host = CreateHost(clubA, clubB);
        host.Mock<IDiscordTextChannelAccess>()
            .DeleteTextChannelAsync(channelId, Arg.Any<CancellationToken>())
            .Returns(true);

        // A null TargetClubId is exactly what SyncClubs emits for a member no longer in any roster.
        var snapshot = new ClubMemberSyncSnapshot(userId, nickname, null, 100, DateTimeOffset.UtcNow.AddMonths(-3));

        await host.SendAsync(new SaveClubMembersCommand([snapshot]));

        await using var read = fixture.CreateDbContext();
        var member = await new EfClubMemberRepository(read).ReadClubMemberByUserIdAsync(userId);
        member!.ClubId.Should().BeNull();
        member.PrivateTextChannelId.Should().BeNull("the deleted channel id should be cleared on success");

        await host.Mock<IDiscordServerRolesAccess>()
            .Received()
            .RemoveRolesFromUserAsync(
                discordId, Arg.Is<IEnumerable<ulong>>(roles => roles.Contains(RoleA)), Arg.Any<CancellationToken>());
        await host.Mock<IDiscordTextChannelAccess>()
            .Received()
            .DeleteTextChannelAsync(channelId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveClubMembers_MemberSwitchesClubs_SwapsRolesAndKeepsPrivateChannel()
    {
        var clubA = Guid.NewGuid();
        var clubB = Guid.NewGuid();
        await SeedClubsAsync(clubA, clubB);

        const ulong channelId = 888UL;
        var (userId, nickname, discordId) = await SeedLinkedMemberAsync(clubId: clubA, discordId: NewDiscordId(), privateChannelId: channelId);

        using var host = CreateHost(clubA, clubB);
        var snapshot = new ClubMemberSyncSnapshot(userId, nickname, clubB, 100, DateTimeOffset.UtcNow.AddMonths(-3));

        await host.SendAsync(new SaveClubMembersCommand([snapshot]));

        await using var read = fixture.CreateDbContext();
        var member = await new EfClubMemberRepository(read).ReadClubMemberByUserIdAsync(userId);
        member!.ClubId.Should().Be(clubB);
        member.PrivateTextChannelId.Should().Be(channelId, "switching clubs must not remove the private channel");

        var roles = host.Mock<IDiscordServerRolesAccess>();
        await roles.Received()
            .RemoveRolesFromUserAsync(
                discordId, Arg.Is<IEnumerable<ulong>>(r => r.Contains(RoleA)), Arg.Any<CancellationToken>());
        await roles.Received()
            .AddRoleToMembersByUserIdsAsync(
                Arg.Is<IEnumerable<ulong>>(ids => ids.Contains(discordId)), RoleB, Arg.Any<CancellationToken>());

        // No PlayerSwitchedClubsEvent handler touches private channels, so neither create nor delete fires.
        await host.Mock<IDiscordTextChannelAccess>()
            .DidNotReceive()
            .DeleteTextChannelAsync(Arg.Any<ulong>(), Arg.Any<CancellationToken>());
        await host.Mock<IDiscordTextChannelAccess>()
            .DidNotReceive()
            .CreatePrivateTextChannelAsync(
                Arg.Any<ulong>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<IEnumerable<ulong>?>(), Arg.Any<IEnumerable<ulong>?>(), Arg.Any<CancellationToken>());
    }
}
