using Utilities;

namespace UseCases.OutputPorts.AI;

/// <summary>
/// Caches the provider's free-model roster and turns it into per-request fallback chains.
///
/// Kept behind a port because the roster changes on the provider's schedule, not ours: the cache is
/// refreshed on a timer, survives provider outages via a persisted snapshot, and degrades to the
/// fallback router when it has nothing at all.
/// </summary>
public interface IChatModelCatalog
{
    /// <summary>
    /// The ordered model chain to send for a request with these needs. Falls back to the router when
    /// too few vetted models qualify, so it is empty only when the requirements exclude the router too
    /// — a retry that has already tried everything.
    /// </summary>
    Task<IReadOnlyList<string>> ReadChainAsync(ChatModelRequirements requirements, CancellationToken cancellationToken = default);

    /// <summary>Ranked candidates with their scores, for operator-facing diagnostics.</summary>
    Task<IReadOnlyList<RankedChatModel>> ReadRankingAsync(ChatModelRequirements requirements, CancellationToken cancellationToken = default);

    /// <summary>Re-reads the roster from upstream. Called at start-up and on a schedule.</summary>
    Task<Result<int>> RefreshAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Demotes a model after an upstream failure so the next chain prefers something else, without
    /// blocking it permanently.
    /// </summary>
    void ReportFailure(string modelId);

    /// <summary>
    /// Demotes the models of <paramref name="chain"/> that must have failed for
    /// <paramref name="answeredBy"/> to answer — or all of them when nothing did. Only the catalog can
    /// tell which entry is the router and which ids are real models, so the inference lives here.
    /// </summary>
    void ReportChainOutcome(IReadOnlyList<string> chain, string? answeredBy);

    /// <summary>
    /// Whether an answer from this model must be discarded: it is blocked, known not to be a chat
    /// model, or has already answered with something that was not an answer. A model the roster does
    /// not know is trusted.
    /// </summary>
    bool IsUnfitToAnswer(string modelId);

    /// <summary>
    /// Keeps a model out of every chain until restart, after it answered with something that was not
    /// an answer — a model the description-based rules failed to recognise as a classifier.
    /// </summary>
    void ReportUnfit(string modelId);

    AiCatalogStatus ReadStatus();
}

/// <param name="Source">Where the current roster came from — live, a persisted snapshot, or nothing.</param>
/// <param name="LearnedUnfitModelIds">
/// Models excluded at runtime after answering with something that was not an answer. They are
/// forgotten on restart, so each one is a candidate for <c>AI:OpenRouter:BlockedModelIds</c>.
/// </param>
public sealed record AiCatalogStatus(
    int ModelCount,
    int VisionModelCount,
    DateTimeOffset? LastRefreshedAtUtc,
    AiCatalogSource Source,
    IReadOnlyList<string> LearnedUnfitModelIds);

public enum AiCatalogSource
{
    /// <summary>No roster at all; only the fallback router is usable.</summary>
    None = 0,
    Live,
    Snapshot
}
