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
    /// The ordered model chain to send for a request with these needs. Never empty — worst case it is
    /// the fallback router alone.
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

    AiCatalogStatus ReadStatus();
}

/// <param name="Source">Where the current roster came from — live, a persisted snapshot, or nothing.</param>
public sealed record AiCatalogStatus(
    int ModelCount,
    int VisionModelCount,
    DateTimeOffset? LastRefreshedAtUtc,
    AiCatalogSource Source);

public enum AiCatalogSource
{
    /// <summary>No roster at all; only the fallback router is usable.</summary>
    None = 0,
    Live,
    Snapshot
}
