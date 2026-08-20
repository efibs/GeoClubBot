namespace Configuration;

/// <summary>
/// Settings for the OpenRouter chat provider. Nested under <see cref="AiConfiguration"/> rather than
/// bound as its own section, so the whole AI feature stays switchable from one flag.
/// </summary>
public class OpenRouterConfiguration
{
    public string? ApiKey { get; set; }

    public string BaseUrl { get; set; } = "https://openrouter.ai";

    /// <summary>
    /// Upstream requests allowed per UTC day. The provider's free tier allows 50/day below $10 of
    /// lifetime credit and 1000/day above it, so the default sits just under the lower ceiling.
    /// Raise this after topping up, otherwise the bot throttles itself far below what is available.
    /// </summary>
    public int DailyRequestBudget { get; set; } = 45;

    /// <summary>Kept just under the provider's 20/min ceiling; enforced by a waiting rate limiter.</summary>
    public int PerMinuteRequestBudget { get; set; } = 18;

    /// <summary>Model id prefixes that outrank everything else, e.g. "google/". Lets an operator pin a family without chasing version suffixes.</summary>
    public List<string> PreferredModelPrefixes { get; set; } = [];

    public List<string> BlockedModelIds { get; set; } = [];

    /// <summary>Router model appended to every chain as the last resort; it self-filters for the features a request needs.</summary>
    public string FallbackModelId { get; set; } = "openrouter/free";

    /// <summary>Ranked candidates sent ahead of the fallback, for server-side failover.</summary>
    public int ChainLength { get; set; } = 3;

    /// <summary>Models with a smaller context window are not considered; RAG prompts are large.</summary>
    public int MinContextLength { get; set; } = 8192;

    /// <summary>Models retiring sooner than this are excluded so a chain cannot go stale mid-day.</summary>
    public int ExpiryHorizonHours { get; set; } = 48;

    /// <summary>Sent as HTTP-Referer; OpenRouter uses it for attribution on their leaderboards.</summary>
    public string? SiteUrl { get; set; }

    /// <summary>Sent as X-Title.</summary>
    public string AppName { get; set; } = "GeoClubBot";
}
