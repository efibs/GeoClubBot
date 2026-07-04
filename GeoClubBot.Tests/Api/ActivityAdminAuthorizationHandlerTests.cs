using System.Security.Claims;
using Configuration;
using FluentAssertions;
using GeoClubBot.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;
using UseCases.OutputPorts.Discord;
using Xunit;

namespace GeoClubBot.Tests.Api;

public sealed class ActivityAdminAuthorizationHandlerTests
{
    private const ulong UserId = 4242UL;

    private readonly IDiscordMemberPermissionAccess _permissions = Substitute.For<IDiscordMemberPermissionAccess>();

    private async Task<AuthorizationHandlerContext> HandleAsync(
        ClaimsPrincipal user,
        DiscordActivityConfiguration config,
        string environmentName)
    {
        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName = environmentName;

        var handler = new ActivityAdminAuthorizationHandler(_permissions, env, Options.Create(config));
        var context = new AuthorizationHandlerContext([new ActivityAdminRequirement()], user, resource: null);

        await handler.HandleAsync(context);

        return context;
    }

    private static ClaimsPrincipal UserWithDiscordId(ulong id) =>
        new(new ClaimsIdentity(
            [new Claim(DiscordActivityAuthenticationHandler.DiscordUserIdClaimType, id.ToString())],
            DiscordActivityAuthenticationHandler.SchemeName));

    [Fact]
    public async Task Grants_when_the_user_is_a_guild_administrator()
    {
        _permissions.IsAdministratorAsync(UserId, Arg.Any<CancellationToken>()).Returns(true);

        var context = await HandleAsync(UserWithDiscordId(UserId), new DiscordActivityConfiguration(), "Production");

        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task Denies_when_the_user_is_not_a_guild_administrator()
    {
        _permissions.IsAdministratorAsync(UserId, Arg.Any<CancellationToken>()).Returns(false);

        var context = await HandleAsync(UserWithDiscordId(UserId), new DiscordActivityConfiguration(), "Production");

        context.HasSucceeded.Should().BeFalse();
    }

    [Fact]
    public async Task Denies_without_a_discord_identity_and_never_asks_discord()
    {
        // An authenticated principal that somehow lacks the discord_user_id claim must be denied
        // without even consulting the permission port — fail closed.
        var context = await HandleAsync(new ClaimsPrincipal(new ClaimsIdentity()), new DiscordActivityConfiguration(), "Production");

        context.HasSucceeded.Should().BeFalse();
        await _permissions.DidNotReceive().IsAdministratorAsync(Arg.Any<ulong>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DevBypass_grants_admin_in_development_without_asking_discord()
    {
        var config = new DiscordActivityConfiguration { DevUserId = UserId, DevUserIsAdmin = true };

        var context = await HandleAsync(UserWithDiscordId(UserId), config, "Development");

        context.HasSucceeded.Should().BeTrue();
        await _permissions.DidNotReceive().IsAdministratorAsync(Arg.Any<ulong>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DevBypass_is_ignored_outside_development()
    {
        var config = new DiscordActivityConfiguration { DevUserId = UserId, DevUserIsAdmin = true };

        var context = await HandleAsync(UserWithDiscordId(UserId), config, "Production");

        // Falls through to the real permission check, which denies.
        context.HasSucceeded.Should().BeFalse();
        await _permissions.Received(1).IsAdministratorAsync(UserId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DevBypass_requires_the_explicit_admin_opt_in()
    {
        // DevUserId alone (the authentication bypass) must not imply dashboard admin.
        var config = new DiscordActivityConfiguration { DevUserId = UserId, DevUserIsAdmin = false };

        var context = await HandleAsync(UserWithDiscordId(UserId), config, "Development");

        context.HasSucceeded.Should().BeFalse();
    }

    [Fact]
    public async Task DevBypass_requires_the_matching_dev_user()
    {
        var config = new DiscordActivityConfiguration { DevUserId = 999UL, DevUserIsAdmin = true };

        var context = await HandleAsync(UserWithDiscordId(UserId), config, "Development");

        context.HasSucceeded.Should().BeFalse();
    }
}
