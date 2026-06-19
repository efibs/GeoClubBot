using System.Text.Encodings.Web;
using Configuration;
using FluentAssertions;
using GeoClubBot.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using UseCases.OutputPorts.Discord;
using Utilities;
using Xunit;

namespace GeoClubBot.Tests.Api;

public sealed class DiscordActivityAuthenticationHandlerTests
{
    private const string DevToken = DiscordActivityAuthenticationHandler.DevBypassToken;

    private readonly IDiscordOAuthService _oauth = Substitute.For<IDiscordOAuthService>();

    private async Task<DiscordActivityAuthenticationHandler> CreateHandlerAsync(
        DiscordActivityConfiguration config,
        string environmentName,
        HttpContext context)
    {
        var options = Substitute.For<IOptionsMonitor<AuthenticationSchemeOptions>>();
        options.Get(Arg.Any<string>()).Returns(new AuthenticationSchemeOptions());

        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName = environmentName;

        var handler = new DiscordActivityAuthenticationHandler(
            options,
            NullLoggerFactory.Instance,
            UrlEncoder.Default,
            _oauth,
            new MemoryCache(new MemoryCacheOptions()),
            env,
            Options.Create(config));

        await handler.InitializeAsync(
            new AuthenticationScheme(
                DiscordActivityAuthenticationHandler.SchemeName,
                displayName: null,
                typeof(DiscordActivityAuthenticationHandler)),
            context);

        return handler;
    }

    private static HttpContext ContextWithBearer(string? token)
    {
        var context = new DefaultHttpContext();
        if (token is not null)
        {
            context.Request.Headers.Authorization = $"Bearer {token}";
        }
        return context;
    }

    [Fact]
    public async Task DevBypass_authenticates_as_dev_user_without_calling_discord_in_development()
    {
        var config = new DiscordActivityConfiguration { Enabled = true, DevUserId = 4242UL };
        var handler = await CreateHandlerAsync(config, "Development", ContextWithBearer(DevToken));

        var result = await handler.AuthenticateAsync();

        result.Succeeded.Should().BeTrue();
        result.Principal!.GetDiscordUserId().Should().Be(4242UL);
        await _oauth.DidNotReceive().GetUserIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DevBypass_is_ignored_outside_development()
    {
        var config = new DiscordActivityConfiguration { Enabled = true, DevUserId = 4242UL };
        _oauth.GetUserIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<ulong>.Failure(Error.Unauthorized("x", "bad")));
        var handler = await CreateHandlerAsync(config, "Production", ContextWithBearer(DevToken));

        var result = await handler.AuthenticateAsync();

        result.Succeeded.Should().BeFalse();
        await _oauth.Received().GetUserIdAsync(DevToken, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DevBypass_is_ignored_when_dev_user_not_configured()
    {
        var config = new DiscordActivityConfiguration { Enabled = true, DevUserId = null };
        _oauth.GetUserIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<ulong>.Failure(Error.Unauthorized("x", "bad")));
        var handler = await CreateHandlerAsync(config, "Development", ContextWithBearer(DevToken));

        var result = await handler.AuthenticateAsync();

        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task Valid_discord_token_authenticates_via_oauth()
    {
        var config = new DiscordActivityConfiguration { Enabled = true };
        _oauth.GetUserIdAsync("real-token", Arg.Any<CancellationToken>())
            .Returns(Result<ulong>.Success(999UL));
        var handler = await CreateHandlerAsync(config, "Production", ContextWithBearer("real-token"));

        var result = await handler.AuthenticateAsync();

        result.Succeeded.Should().BeTrue();
        result.Principal!.GetDiscordUserId().Should().Be(999UL);
    }

    [Fact]
    public async Task Disabled_activity_fails_authentication()
    {
        var config = new DiscordActivityConfiguration { Enabled = false, DevUserId = 4242UL };
        var handler = await CreateHandlerAsync(config, "Development", ContextWithBearer(DevToken));

        var result = await handler.AuthenticateAsync();

        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task Missing_authorization_header_yields_no_result()
    {
        var config = new DiscordActivityConfiguration { Enabled = true };
        var handler = await CreateHandlerAsync(config, "Production", ContextWithBearer(null));

        var result = await handler.AuthenticateAsync();

        result.None.Should().BeTrue();
    }
}
