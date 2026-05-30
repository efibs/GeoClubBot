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
}
