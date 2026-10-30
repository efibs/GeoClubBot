using Configuration;
using Entities.Events;
using FluentAssertions;
using GeoClubBot.Tests.TestBuilders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using UseCases.OutputPorts.Discord;
using UseCases.UseCases.ClubMemberRole;
using Xunit;

namespace GeoClubBot.Tests.Application.UseCases.ClubMemberRole;

/// <summary>Club roles follow the member. These run on domain events, so none of them may ever throw.</summary>
public sealed class ClubMembershipRoleHandlersTests
{
    private const string UserId = "user-1";
    private const string Nickname = "Player1";
    private const ulong DiscordUserId = 42;
    private const ulong MainRole = 10;
    private const ulong SecondRole = 20;

    private static readonly Guid MainClub = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid SecondClub = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid RolelessClub = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    private readonly IDiscordServerRolesAccess _roles = Substitute.For<IDiscordServerRolesAccess>();

    private readonly IOptions<GeoGuessrConfiguration> _config = new GeoGuessrConfigurationBuilder()
        .WithClub(MainClub, roleId: MainRole)
        .WithClub(SecondClub, isMain: false, roleId: SecondRole)
        .WithClub(RolelessClub, isMain: false)
        .BuildOptions();

    private Task AddedRoleShouldBe(ulong roleId) =>
        _roles.Received(1).AddRoleToMembersByUserIdsAsync(
            Arg.Is<IEnumerable<ulong>>(ids => ids.SequenceEqual(new[] { DiscordUserId })), roleId, Arg.Any<CancellationToken>());

    private Task RemovedRoleShouldBe(params ulong[] roleIds) =>
        _roles.Received(1).RemoveRolesFromUserAsync(
            DiscordUserId, Arg.Is<IEnumerable<ulong>>(ids => ids.SequenceEqual(roleIds)), Arg.Any<CancellationToken>());

    // ---- Joined -----------------------------------------------------------

    private Task JoinAsync(ulong? discordUserId, Guid clubId) =>
        new HandlePlayerJoinedClubForMemberRoleUseCase(_roles, _config, NullLogger<HandlePlayerJoinedClubForMemberRoleUseCase>.Instance)
            .Handle(new PlayerJoinedClubEvent(UserId, Nickname, discordUserId, clubId, null), CancellationToken.None);

    [Fact]
    public async Task Join_GivesTheClubsRole()
    {
        await JoinAsync(DiscordUserId, SecondClub);

        await AddedRoleShouldBe(SecondRole);
    }

    [Fact]
    public async Task Join_OfAnUnlinkedPlayer_GivesNothing()
    {
        await JoinAsync(null, SecondClub);

        _roles.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Join_OfAClubWithoutARole_GivesNothing()
    {
        await JoinAsync(DiscordUserId, RolelessClub);

        _roles.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Join_NeverThrows()
    {
        await JoinAsync(DiscordUserId, Guid.NewGuid());

        _roles.ReceivedCalls().Should().BeEmpty();
    }

    // ---- Left -------------------------------------------------------------

    private Task LeaveAsync(ulong? discordUserId, Guid clubId) =>
        new HandlePlayerLeftClubForMemberRoleUseCase(_roles, _config, NullLogger<HandlePlayerLeftClubForMemberRoleUseCase>.Instance)
            .Handle(new PlayerLeftClubEvent(UserId, Nickname, discordUserId, clubId, null), CancellationToken.None);

    [Fact]
    public async Task Leave_TakesTheClubsRoleAway()
    {
        await LeaveAsync(DiscordUserId, SecondClub);

        await RemovedRoleShouldBe(SecondRole);
    }

    [Fact]
    public async Task Leave_OfAnUnlinkedPlayer_TakesNothing()
    {
        await LeaveAsync(null, SecondClub);

        _roles.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Leave_OfAClubWithoutARole_TakesNothing()
    {
        await LeaveAsync(DiscordUserId, RolelessClub);

        _roles.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Leave_NeverThrows()
    {
        await LeaveAsync(DiscordUserId, Guid.NewGuid());

        _roles.ReceivedCalls().Should().BeEmpty();
    }

    // ---- Switched ---------------------------------------------------------

    private Task SwitchAsync(ulong? discordUserId, Guid oldClub, Guid newClub) =>
        new HandlePlayerSwitchedClubsForMemberRoleUseCase(_roles, _config, NullLogger<HandlePlayerSwitchedClubsForMemberRoleUseCase>.Instance)
            .Handle(new PlayerSwitchedClubsEvent(UserId, Nickname, discordUserId, oldClub, newClub), CancellationToken.None);

    [Fact]
    public async Task Switch_SwapsTheRoles()
    {
        await SwitchAsync(DiscordUserId, MainClub, SecondClub);

        await RemovedRoleShouldBe(MainRole);
        await AddedRoleShouldBe(SecondRole);
    }

    [Fact]
    public async Task Switch_OfAnUnlinkedPlayer_TouchesNothing()
    {
        await SwitchAsync(null, MainClub, SecondClub);

        _roles.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Switch_FromAClubWithoutARole_OnlyGivesTheNewOne()
    {
        await SwitchAsync(DiscordUserId, RolelessClub, SecondClub);

        await _roles.DidNotReceiveWithAnyArgs().RemoveRolesFromUserAsync(default, default!, default);
        await AddedRoleShouldBe(SecondRole);
    }

    [Fact]
    public async Task Switch_ToAClubWithoutARole_OnlyTakesTheOldOne()
    {
        await SwitchAsync(DiscordUserId, MainClub, RolelessClub);

        await RemovedRoleShouldBe(MainRole);
        await _roles.DidNotReceiveWithAnyArgs().AddRoleToMembersByUserIdsAsync(default!, default, default);
    }

    [Fact]
    public async Task Switch_NeverThrows()
    {
        _roles.RemoveRolesFromUserAsync(Arg.Any<ulong>(), Arg.Any<IEnumerable<ulong>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("discord is down"));

        await SwitchAsync(DiscordUserId, MainClub, SecondClub);
    }

    // ---- Unlinked ---------------------------------------------------------

    private Task UnlinkAsync(IOptions<GeoGuessrConfiguration> config) =>
        new HandleAccountUnlinkedForMemberRoleUseCase(_roles, config, NullLogger<HandleAccountUnlinkedForMemberRoleUseCase>.Instance)
            .Handle(new AccountUnlinkedEvent(UserId, Nickname, DiscordUserId), CancellationToken.None);

    [Fact]
    public async Task Unlink_TakesEveryClubsRoleAway()
    {
        await UnlinkAsync(_config);

        await RemovedRoleShouldBe(MainRole, SecondRole);
    }

    [Fact]
    public async Task Unlink_WithoutAnyClubRole_TouchesNothing()
    {
        await UnlinkAsync(new GeoGuessrConfigurationBuilder().WithClub(RolelessClub).BuildOptions());

        _roles.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Unlink_NeverThrows()
    {
        _roles.RemoveRolesFromUserAsync(Arg.Any<ulong>(), Arg.Any<IEnumerable<ulong>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("discord is down"));

        await UnlinkAsync(_config);
    }
}
