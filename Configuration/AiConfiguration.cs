namespace Configuration;

public class AiConfiguration
{
    public const string SectionName = "AI";

    /// <summary>
    /// Master switch for the optional AI features. When false the AI services are not registered,
    /// so the other values in this section are not required.
    /// </summary>
    public bool Active { get; set; }

    public string? LlmModel { get; set; }

    public string? CategorizeModel { get; set; }

    public string? LlmApiKey { get; set; }

    public string? EmbeddingModel { get; set; }

    public int MaxDegreeOfParallelism { get; set; } = 4;

    public int RequestTimeoutSeconds { get; set; } = 60;

    public int OverallTimeoutSeconds { get; set; } = 180;

    /// <summary>Chat provider settings. Embeddings are computed in-process and need no provider.</summary>
    public OpenRouterConfiguration OpenRouter { get; set; } = new();

    /// <summary>Upstream AI calls allowed to run at once, bounding load on the provider and on us.</summary>
    public int MaxConcurrentRequests { get; set; } = 4;

    /// <summary>Per-user hourly cap, so one person cannot spend the guild's whole daily allowance.</summary>
    public int MaxRequestsPerUserPerHour { get; set; } = 6;
}
