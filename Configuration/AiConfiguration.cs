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
    /// Longest a single request to the AI provider may take to start answering. Enforced per attempt,
    /// so a hung attempt is retried instead of consuming the whole call. It stops counting once the
    /// response has opened: see <see cref="ResponseTimeoutSeconds"/>.
    /// </summary>
    public int RequestTimeoutSeconds { get; set; } = 60;

    /// <summary>
    /// Longest a response may take to arrive in full once it has opened. The provider opens a chat
    /// completion's response at once and holds it open while the model works, so this is how long a
    /// model may take to write one reply; a model still going after it is cut off, demoted for a
    /// while, and other models are asked. Nothing bounded this before 2026-11-11, when a stalled model
    /// held a response open for 57 minutes. Of 385 replayed replies that answered, the slowest ordinary
    /// one took 68 seconds; the two beyond that were a runaway of 63,000 tokens (145 s) and one of 180 s.
    /// Keep <see cref="OverallTimeoutSeconds"/> well above it, or the call's own limit ends a slow reply
    /// first and it is reported as an unreachable provider, which asks no other model.
    /// </summary>
    public int ResponseTimeoutSeconds { get; set; } = 90;

    /// <summary>
    /// Longest a call may take in total: queueing for a rate-limit token, waiting out the provider's
    /// per-minute window after a 429, the retries, and reading the response. Must comfortably exceed
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
