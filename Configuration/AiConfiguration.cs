namespace Configuration;

public class AiConfiguration
{
    public const string SectionName = "AI";

    /// <summary>
    /// Master switch for the optional AI features. When false the AI services are not registered,
    /// so the other values in this section are not required.
    /// </summary>
    public bool Active { get; set; }

    /// <summary>
    /// Longest a single request to the AI provider may take. Enforced per attempt, so a hung attempt is
    /// retried instead of consuming the whole call.
    /// </summary>
    public int RequestTimeoutSeconds { get; set; } = 60;

    /// <summary>
    /// Longest a call may take in total: queueing for a rate-limit token, waiting out the provider's
    /// per-minute window after a 429, and the retries. Must comfortably exceed
    /// <see cref="RequestTimeoutSeconds"/> plus a minute, or a rate-limited request is abandoned just
    /// before the retry that would have succeeded.
    /// </summary>
    public int OverallTimeoutSeconds { get; set; } = 180;

    /// <summary>Chat provider settings. Embeddings are computed in-process and need no provider.</summary>
    public OpenRouterConfiguration OpenRouter { get; set; } = new();

    /// <summary>Upstream AI calls allowed to run at once, bounding load on the provider and on us.</summary>
    public int MaxConcurrentRequests { get; set; } = 4;

    /// <summary>Per-user hourly cap, so one person cannot spend the guild's whole daily allowance.</summary>
    public int MaxRequestsPerUserPerHour { get; set; } = 6;

    /// <summary>
    /// Guide pictures attached to a single answer, counted in pictures rather than cited excerpts, so a
    /// reply cannot turn into an image dump. Every cited excerpt's first picture is attached before any
    /// excerpt's second. Six rather than the three it was: an excerpt can now bring several pictures
    /// (one slide caption beside each picture of its slide), and 9 of the 25 ratings in the 2026-10-23
    /// export asked for pictures — mostly ones that had failed to show, once for "more images". Discord
    /// shows at most ten per message.
    /// </summary>
    public int MaxImagesInReply { get; set; } = 6;

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
