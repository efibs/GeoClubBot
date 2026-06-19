using Utilities;

namespace UseCases.OutputPorts.Discord;

/// <summary>
/// Output port for the Discord OAuth2 exchange used by the Club Dashboard Activity. The embedded
/// frontend obtains an authorization <c>code</c> from the Embedded App SDK and hands it to the
/// backend, which exchanges it for an access token using the application's client secret and can
/// resolve the authenticated Discord user from that token.
/// </summary>
public interface IDiscordOAuthService
{
    /// <summary>
    /// Exchanges an OAuth2 authorization code for a Discord access token.
    /// </summary>
    Task<Result<string>> ExchangeCodeForTokenAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves the Discord user id that owns the given access token (via <c>GET /users/@me</c>).
    /// </summary>
    Task<Result<ulong>> GetUserIdAsync(string accessToken, CancellationToken cancellationToken = default);
}
