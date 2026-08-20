namespace UseCases.OutputPorts.AI;

/// <summary>What a particular request needs from a model. Used to filter the candidate pool.</summary>
public sealed record ChatModelRequirements(
    bool NeedsImageInput = false,
    bool NeedsTools = false,
    int MinContextLength = 8192);

/// <summary>
/// Operator-controlled knobs for <see cref="ChatModelSelector"/>. Prefixes rather than exact ids so
/// an operator can pin a family (<c>google/</c>) without chasing version suffixes.
/// </summary>
public sealed record ChatModelSelectionOptions
{
    public IReadOnlyList<string> PreferredModelPrefixes { get; init; } = [];

    public IReadOnlySet<string> BlockedModelIds { get; init; } = new HashSet<string>();

    /// <summary>
    /// Router model used as the final link of every chain. It picks a free model at random and
    /// self-filters for the features the request needs, so it is the safest possible last resort.
    /// </summary>
    public string FallbackModelId { get; init; } = "openrouter/free";

    /// <summary>How many ranked candidates precede the fallback in the chain.</summary>
    public int ChainLength { get; init; } = 3;

    /// <summary>Models retiring sooner than this are excluded outright.</summary>
    public TimeSpan ExpiryHorizon { get; init; } = TimeSpan.FromHours(48);
}
