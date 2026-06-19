namespace Configuration;

/// <summary>
/// OAuth2 credentials for the Club Dashboard Discord Activity (the embedded web app).
/// Optional — bound without start-up validation and gated by <see cref="Enabled"/> so the host
/// boots in dev/test without a client secret. The activity's data endpoints and frontend are only
/// served when <see cref="Enabled"/> is <c>true</c> and the credentials are present.
/// </summary>
public class DiscordActivityConfiguration
{
    public const string SectionName = "DiscordActivity";

    /// <summary>When false, the activity frontend is not served and the OAuth handshake is rejected.</summary>
    public bool Enabled { get; set; }

    /// <summary>The Discord application (client) id, used for the OAuth2 token exchange.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>The Discord application client secret, used for the OAuth2 token exchange.</summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// Development-only: when set (and the host runs in the Development environment), a request
    /// bearing the frontend's bypass token is authenticated as this Discord user id WITHOUT
    /// contacting Discord. Pairs with the frontend's <c>VITE_DEV_BYPASS</c> so the activity can be
    /// run locally without the Discord SDK. Set it to your own Discord user id to also exercise the
    /// "highlight the viewer" path. Leave <c>null</c> in production.
    /// </summary>
    public ulong? DevUserId { get; set; }
}
