using System.ComponentModel.DataAnnotations;

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

    /// <summary>
    /// Model id prefixes that outrank everything else, e.g. "google/", best first: a model matching an
    /// earlier prefix outranks one matching a later prefix. Lets an operator pin a family without
    /// chasing version suffixes. A preference orders the models that qualify; it does not let in one
    /// the size or uptime bar keeps out, and a preferred model that keeps failing still falls behind.
    /// Blank entries are ignored, which is how an environment override drops a shipped one.
    /// </summary>
    public List<string> PreferredModelPrefixes { get; set; } = [];

    /// <summary>
    /// Models never to ask. Models that answer with something that is not an answer, or refuse to serve
    /// this bot at all (a 403 "only available on agentic harnesses"), are also excluded at runtime, until
    /// restart — <c>/ai status</c> lists them as candidates for this list. A refusal is learned only when
    /// it reaches the bot, which OpenRouter's failover usually prevents, so a known one belongs here.
    /// Blank entries are ignored, which is how an environment override drops a shipped one.
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
    /// Smallest model asked, in billions of effective parameters (the geometric mean of total and active
    /// parameters, so a mixture of experts compares with a dense model). Read from the model's id, name
    /// and description; a model that states no size is not held to it. Ignored for a request no model
    /// could otherwise answer. Negative values count as 0.
    /// </summary>
    public double MinEffectiveParametersBillions { get; set; } = 8;

    /// <summary>
    /// Models whose best provider served less than this share of the last day's requests (0–100) are not
    /// asked. Measured on each catalog refresh; a model whose health could not be read counts as
    /// healthy, and the bar is ignored for a request no model could otherwise answer.
    /// </summary>
    public double MinProviderUptimePercent { get; set; } = 50;

    /// <summary>
    /// Models whose provider health is read on each catalog refresh, taken from the head of the text and
    /// vision rankings. One request each, through the same rate limiter as questions, so it is bounded
    /// rather than the whole roster: values above 20 are clamped to 20, and 0 turns the check off.
    /// </summary>
    public int HealthCheckModelCount { get; set; } = 10;

    /// <summary>
    /// Embedding model used while OpenRouter serves it. Must produce vectors matching
    /// <see cref="EmbeddingDimensions"/>. Each model and width has a Qdrant collection of its own, so a
    /// change starts a fresh one and the library is indexed again into it, source by source.
    /// </summary>
    public string EmbeddingModelId { get; set; } = "nvidia/nemotron-3.5-embed-1b:free";

    /// <summary>
    /// Width of <see cref="EmbeddingModelId"/>'s vectors. 0 learns it from the model with one embedding
    /// request when the model is first chosen.
    /// </summary>
    [Range(0, 65536)]
    public int EmbeddingDimensions { get; set; } = 2048;

    /// <summary>
    /// Models to move to, in order, when OpenRouter no longer serves the one in use (it answers 404 "No
    /// endpoints found", or its embedding catalog leaves the model out). Free models come and go: the
    /// first default, nvidia/llama-nemotron-embed-vl-1b-v2:free, disappeared on 2026-11-16 (#503). A
    /// switch lands on another collection, so every guide is indexed again over the following runs.
    /// </summary>
    public List<EmbeddingModelOption> FallbackEmbeddingModels { get; set; } =
        [new() { ModelId = "nvidia/nemotron-3.5-embed-8b:free" }];

    /// <summary>
    /// When neither <see cref="EmbeddingModelId"/> nor any fallback is served, use a free model from
    /// OpenRouter's embedding catalog that takes both text and images, its width learned with one
    /// request. Off leaves the AI without embeddings until the configuration names a model that works.
    /// </summary>
    public bool AutoSelectEmbeddingModel { get; set; } = true;

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

/// <summary>An embedding model to fall back on, see <see cref="OpenRouterConfiguration.FallbackEmbeddingModels"/>.</summary>
public class EmbeddingModelOption
{
    [Required]
    public string ModelId { get; set; } = string.Empty;

    /// <summary>Width of the model's vectors; 0 learns it with one embedding request.</summary>
    [Range(0, 65536)]
    public int Dimensions { get; set; }
}
