namespace Configuration;

public class AiConfiguration
{
    public const string SectionName = "AI";

    /// <summary>
    /// Master switch for the optional AI features. When false the AI services are not registered,
    /// so the other values in this section are not required.
    /// </summary>
    public bool Active { get; set; }

    public int RequestTimeoutSeconds { get; set; } = 60;

    public int OverallTimeoutSeconds { get; set; } = 180;

    /// <summary>Chat provider settings. Embeddings are computed in-process and need no provider.</summary>
    public OpenRouterConfiguration OpenRouter { get; set; } = new();

    /// <summary>Upstream AI calls allowed to run at once, bounding load on the provider and on us.</summary>
    public int MaxConcurrentRequests { get; set; } = 4;

    /// <summary>Per-user hourly cap, so one person cannot spend the guild's whole daily allowance.</summary>
    public int MaxRequestsPerUserPerHour { get; set; } = 6;

    /// <summary>Guide images attached to a single answer, so a reply cannot turn into an image dump.</summary>
    public int MaxImagesInReply { get; set; } = 3;

    /// <summary>
    /// Channels the bot will answer in. Empty means every channel it can see. Restricting this is the
    /// simplest way to keep the daily allowance from being spent in unrelated channels.
    /// </summary>
    public List<ulong> AllowedChannelIds { get; set; } = [];

    /// <summary>
    /// Prefix of the vector collection. The embedding model and its dimensions are appended
    /// automatically, because vectors from different models are not comparable: switching models must
    /// start a fresh collection rather than silently mixing incompatible vectors into an existing one.
    /// </summary>
    public string KnowledgeCollectionPrefix { get; set; } = "geo-knowledge";
}
