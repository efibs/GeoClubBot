namespace GeoClubBot;

/// <summary>Named rate-limiting policies registered in Program.cs.</summary>
public static class RateLimitPolicies
{
    /// <summary>
    /// Per-client-IP throttle on the anonymous OAuth2 code → token exchange, the only endpoint an
    /// unauthenticated caller can use to make the server talk to Discord.
    /// </summary>
    public const string ActivityTokenExchange = "activity-token-exchange";

    /// <summary>
    /// Per-client-IP throttle on the anonymous guide-image endpoint. It only serves bytes already on
    /// disk, so the risk is bandwidth rather than compute, but it is the one route reachable by
    /// anybody who learns the public URL.
    /// </summary>
    public const string AiImageRelay = "ai-image-relay";
}
