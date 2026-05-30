using Configuration;
using Entities;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using UseCases.OutputPorts;
using UseCases.OutputPorts.Discord;
using UseCases.UseCases.GeoGuessrAccountLinking;
using Utilities;
using Xunit;
using GeoClubBot.Tests.TestBuilders;

namespace GeoClubBot.Tests.Application.UseCases.GeoGuessrAccountLinking;

public sealed class UnlinkAccountsHandlerTests
{
    private const ulong DiscordUserId = 555UL;
    private const string GeoGuessrUserId = "ggUser-1";
    private const ulong HasLinkedRoleId = 9999UL;
    private const ulong ClubRoleId = 4242UL;
    private static readonly Guid ClubId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private readonly IGeoGuessrUserRepository _users = Substitute.For<IGeoGuessrUserRepository>();
    private readonly IDiscordServerRolesAccess _roles = Substitute.For<IDiscordServerRolesAccess>();

    private UnlinkAccountsHandler CreateHandler()
    {
        var accountLinkingConfig = Options.Create(new GeoGuessrAccountLinkingConfiguration
        {
            AdminChannelId = 0UL,
            HasLinkedRoleId = HasLinkedRoleId
        });

        var geoConfig = new GeoGuessrConfigurationBuilder()
            .WithClub(ClubId, roleId: ClubRoleId)
            .BuildOptions();

        return new UnlinkAccountsHandler(_users, _roles, accountLinkingConfig, geoConfig);
    }

    [Fact]
    public async Task Handle_UnlinksAndRemovesRoles_OnSuccess()
    {
        var user = GeoGuessrUser.Create(GeoGuessrUserId, "Player1", DiscordUserId);
        _users.ReadForUpdateByUserIdAsync(GeoGuessrUserId, Arg.Any<CancellationToken>()).Returns(user);

        var result = await CreateHandler()
            .Handle(new UnlinkAccountsCommand(DiscordUserId, GeoGuessrUserId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        user.DiscordUserId.Should().BeNull();
        await _roles.Received(1).RemoveRolesFromUserAsync(
            DiscordUserId,
            Arg.Is<IEnumerable<ulong>>(ids => ids.Contains(HasLinkedRoleId) && ids.Contains(ClubRoleId)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ReturnsNotFound_WhenUserMissing()
    {
        _users.ReadForUpdateByUserIdAsync(GeoGuessrUserId, Arg.Any<CancellationToken>())
            .Returns((GeoGuessrUser?)null);

        var result = await CreateHandler()
            .Handle(new UnlinkAccountsCommand(DiscordUserId, GeoGuessrUserId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("account_linking.not_linked");
        result.Error.Type.Should().Be(ErrorType.NotFound);
        await _roles.DidNotReceive().RemoveRolesFromUserAsync(
            Arg.Any<ulong>(), Arg.Any<IEnumerable<ulong>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ReturnsNotFound_WhenDiscordIdDoesNotMatch()
    {
        var user = GeoGuessrUser.Create(GeoGuessrUserId, "Player1", discordUserId: 111UL);
        _users.ReadForUpdateByUserIdAsync(GeoGuessrUserId, Arg.Any<CancellationToken>()).Returns(user);

        var result = await CreateHandler()
            .Handle(new UnlinkAccountsCommand(DiscordUserId, GeoGuessrUserId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("account_linking.not_linked");
        user.DiscordUserId.Should().Be(111UL, "a mismatched unlink request must not mutate the user");
    }
}
