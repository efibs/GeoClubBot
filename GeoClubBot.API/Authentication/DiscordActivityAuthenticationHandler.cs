using System.Security.Claims;
using System.Text.Encodings.Web;
using Configuration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using UseCases.OutputPorts.Discord;

namespace GeoClubBot.Authentication;

/// <summary>
/// Authenticates requests from the Club Dashboard Activity by treating the
/// <c>Authorization: Bearer &lt;token&gt;</c> header as a Discord OAuth2 access token. The token is
/// validated against Discord (<c>GET /users/@me</c>) and the resolved Discord user id is cached
/// briefly to avoid hammering Discord on every dashboard refresh. The user id is exposed as the
/// <see cref="DiscordUserIdClaimType"/> claim so controllers can personalize the response.
///
/// For local development a bypass is supported: when the host runs in the Development environment
/// and <see cref="DiscordActivityConfiguration.DevUserId"/> is set, a request bearing
/// <see cref="DevBypassToken"/> (the token the frontend sends in <c>VITE_DEV_BYPASS</c> mode) is
/// authenticated as that user id without contacting Discord.
/// </summary>
public sealed class DiscordActivityAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "DiscordActivity";
    public const string DiscordUserIdClaimType = "discord_user_id";

    /// <summary>The placeholder token the frontend sends when the Discord SDK handshake is bypassed.</summary>
    public const string DevBypassToken = "dev-bypass-token";

    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    private readonly IDiscordOAuthService _oauth;
    private readonly IMemoryCache _cache;
    private readonly IHostEnvironment _environment;
    private readonly DiscordActivityConfiguration _config;

    public DiscordActivityAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IDiscordOAuthService oauth,
        IMemoryCache cache,
        IHostEnvironment environment,
        IOptions<DiscordActivityConfiguration> config) : base(options, logger, encoder)
    {
        _oauth = oauth;
        _cache = cache;
        _environment = environment;
        _config = config.Value;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!_config.Enabled)
        {
            return AuthenticateResult.Fail("The Discord Activity is not enabled.");
        }

        if (!Request.Headers.TryGetValue("Authorization", out var authHeader))
        {
            return AuthenticateResult.NoResult();
        }

        const string prefix = "Bearer ";
        var raw = authHeader.ToString();
        if (!raw.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.NoResult();
        }

        var token = raw[prefix.Length..].Trim();
        if (string.IsNullOrEmpty(token))
        {
            return AuthenticateResult.NoResult();
        }

        // Local-dev bypass: accept the frontend's placeholder token as the configured dev user,
        // skipping Discord entirely. Gated by both the Development environment and explicit config.
        if (_environment.IsDevelopment() && _config.DevUserId is { } devUserId && token == DevBypassToken)
        {
            return Success(devUserId);
        }

        var cacheKey = $"discord-activity-token:{token}";
        if (!_cache.TryGetValue(cacheKey, out ulong userId))
        {
            var result = await _oauth.GetUserIdAsync(token, Context.RequestAborted).ConfigureAwait(false);
            if (result.IsFailure)
            {
                return AuthenticateResult.Fail("The Discord access token is invalid.");
            }

            userId = result.Value;
            _cache.Set(cacheKey, userId, CacheDuration);
        }

        return Success(userId);
    }

    private AuthenticateResult Success(ulong userId)
    {
        var claims = new[]
        {
            new Claim(DiscordUserIdClaimType, userId.ToString()),
            new Claim(ClaimTypes.NameIdentifier, userId.ToString())
        };
        var identity = new ClaimsIdentity(claims, SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return AuthenticateResult.Success(ticket);
    }
}
