using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.OutputPorts.Discord;
using Utilities;

namespace Infrastructure.OutputAdapters.Discord;

/// <summary>
/// Talks to Discord's OAuth2 + REST API over HTTP (the typed <see cref="HttpClient"/> is configured
/// with the <c>https://discord.com/api/</c> base address). Used by the Club Dashboard Activity to
/// turn an authorization code into an access token and to identify the calling user.
/// </summary>
public sealed class DiscordOAuthService(
    HttpClient httpClient,
    IOptions<DiscordActivityConfiguration> config,
    ILogger<DiscordOAuthService> logger) : IDiscordOAuthService
{
    private static readonly Error NotConfigured = Error.Unexpected(
        "discord_activity.not_configured",
        "The Discord Activity OAuth client is not configured.");
    private static readonly Error ExchangeFailed = Error.Unauthorized(
        "discord_activity.token_exchange_failed",
        "Failed to exchange the authorization code for an access token.");
    private static readonly Error IdentifyFailed = Error.Unauthorized(
        "discord_activity.identify_failed",
        "The provided Discord access token is invalid.");

    public async Task<Result<string>> ExchangeCodeForTokenAsync(string code, CancellationToken cancellationToken = default)
    {
        var cfg = config.Value;
        if (string.IsNullOrWhiteSpace(cfg.ClientId) || string.IsNullOrWhiteSpace(cfg.ClientSecret))
        {
            return NotConfigured;
        }

        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = cfg.ClientId,
            ["client_secret"] = cfg.ClientSecret,
            ["grant_type"] = "authorization_code",
            ["code"] = code
        });

        using var response = await httpClient.PostAsync("oauth2/token", content, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Discord token exchange failed with status {StatusCode}.", response.StatusCode);
            return ExchangeFailed;
        }

        var payload = await response.Content
            .ReadFromJsonAsync<TokenResponse>(cancellationToken)
            .ConfigureAwait(false);

        return string.IsNullOrWhiteSpace(payload?.AccessToken)
            ? ExchangeFailed
            : Result<string>.Success(payload.AccessToken);
    }

    public async Task<Result<ulong>> GetUserIdAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "users/@me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return IdentifyFailed;
        }

        var payload = await response.Content
            .ReadFromJsonAsync<CurrentUserResponse>(cancellationToken)
            .ConfigureAwait(false);

        return ulong.TryParse(payload?.Id, out var userId)
            ? Result<ulong>.Success(userId)
            : IdentifyFailed;
    }

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string? AccessToken);

    private sealed record CurrentUserResponse(
        [property: JsonPropertyName("id")] string? Id);
}
