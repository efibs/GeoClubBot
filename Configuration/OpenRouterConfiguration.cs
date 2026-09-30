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

    /// <summary>
    /// Kept just under the provider's 20/min ceiling, which still applies to free models after the $10
    /// top-up. Requests are paced evenly across the minute rather than released in one lump, because a
    /// lump lets a burst straddle the refill and send twice this in a few seconds.
    /// </summary>
    public int PerMinuteRequestBudget { get; set; } = 18;

    /// <summary>Model id prefixes that outrank everything else, e.g. "google/". Lets an operator pin a family without chasing version suffixes.</summary>
    public List<string> PreferredModelPrefixes { get; set; } = [];

    /// <summary>
    /// Models never to ask. Models that answer with something that is not an answer are also excluded
    /// at runtime, until restart — <c>/ai status</c> lists them as candidates for this list.
    /// </summary>
    public List<string> BlockedModelIds { get; set; } = [];

    /// <summary>
    /// Router used only when too few vetted models qualify — an empty or exhausted roster. It picks a
    /// free model at random, safety classifiers and 2B models included, and cannot be told to avoid
    /// any, so its answers are screened like every other.
    /// </summary>
    public string FallbackModelId { get; set; } = "openrouter/free";

    /// <summary>
    /// Vetted models named in one request, for server-side failover. OpenRouter rejects a request
    /// naming more than three outright, so values above 3 are clamped to 3.
    /// </summary>
    public int ChainLength { get; set; } = 3;

    /// <summary>Models with a smaller context window are not considered; RAG prompts are large.</summary>
    public int MinContextLength { get; set; } = 8192;

    /// <summary>Models retiring sooner than this are excluded so a chain cannot go stale mid-day.</summary>
    public int ExpiryHorizonHours { get; set; } = 48;

    /// <summary>
    /// Embedding model. Must produce vectors matching <see cref="EmbeddingDimensions"/>; changing
    /// either invalidates every stored vector, so the collection name carries them too.
    /// </summary>
    public string EmbeddingModelId { get; set; } = "nvidia/llama-nemotron-embed-vl-1b-v2:free";

    public int EmbeddingDimensions { get; set; } = 2048;

    /// <summary>
    /// Inputs per embedding request. Batching is what makes indexing affordable: the provider limits
    /// requests, not inputs, so a corpus that would need thousands of calls needs tens.
    /// </summary>
    public int EmbeddingBatchSize { get; set; } = 32;

    /// <summary>
    /// Largest embedding request body, in bytes. Images held by the relay travel inline as base64, so a
    /// batch of large pictures grows fast and is split to stay under this. A 9.6 MB batch of 32 guide
    /// images was accepted when measured, so the default keeps a margin below what is known to work.
    /// </summary>
    public int EmbeddingMaxRequestBytes { get; set; } = 8 * 1024 * 1024;

    /// <summary>Sent as HTTP-Referer; OpenRouter uses it for attribution on their leaderboards.</summary>
    public string? SiteUrl { get; set; }

    /// <summary>Sent as X-Title.</summary>
    public string AppName { get; set; } = "GeoClubBot";
}
