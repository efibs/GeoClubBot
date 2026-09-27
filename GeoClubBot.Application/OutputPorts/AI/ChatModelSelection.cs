namespace UseCases.OutputPorts.AI;

/// <summary>What a particular request needs from a model. Used to filter the candidate pool.</summary>
/// <param name="ExcludedModelIds">
/// Models already tried for this question, so that a retry asks different ones. Carried here rather
/// than as an argument of its own because it is one more reason a model is not a candidate for this
/// particular request — and so every existing caller of the catalog keeps working unchanged.
/// Compared case-insensitively whatever comparer the set was built with.
/// </param>
public sealed record ChatModelRequirements(
    bool NeedsImageInput = false,
    bool NeedsTools = false,
    int MinContextLength = 8192,
    IReadOnlySet<string>? ExcludedModelIds = null);

/// <summary>
/// Operator-controlled knobs for <see cref="ChatModelSelector"/>. Prefixes rather than exact ids so
/// an operator can pin a family (<c>google/</c>) without chasing version suffixes.
/// </summary>
public sealed record ChatModelSelectionOptions
{
    public IReadOnlyList<string> PreferredModelPrefixes { get; init; } = [];

    public IReadOnlySet<string> BlockedModelIds { get; init; } = new HashSet<string>();

    /// <summary>
    /// Router model used only when too few vetted models qualify. It picks a free model at random —
    /// safety classifiers and 2B models included — and cannot be told to avoid any, which is exactly
    /// how a classifier's "User Safety: safe" once reached users as an answer. It is a last resort for
    /// an empty or exhausted roster, never a routine member of the chain.
    /// </summary>
    public string FallbackModelId { get; init; } = "openrouter/free";

    /// <summary>
    /// How many vetted models one request names, for server-side failover. The router only fills slots
    /// that no vetted model can.
    /// </summary>
    public int ChainLength { get; init; } = 3;

    /// <summary>Models retiring sooner than this are excluded outright.</summary>
    public TimeSpan ExpiryHorizon { get; init; } = TimeSpan.FromHours(48);
}
