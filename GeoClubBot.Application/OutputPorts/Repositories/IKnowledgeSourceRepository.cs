using Entities;

namespace UseCases.OutputPorts.Repositories;

public interface IKnowledgeSourceRepository
{
    Task<KnowledgeSource?> ReadAsync(string sourceType, string naturalKey, CancellationToken cancellationToken = default);

    /// <param name="ignoreSchedule">
    /// Bypasses the re-ingest interval and failure backoff, for an operator-triggered run. Without it
    /// a manual "force" does nothing at all on sources that were indexed recently.
    /// </param>
    Task<IReadOnlyList<KnowledgeSource>> ReadDueForIngestAsync(
        DateTimeOffset nowUtc,
        TimeSpan reingestInterval,
        int limit,
        bool ignoreSchedule = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Every known source, tracked so the caller's updates are persisted. Used by the catalogue sync,
    /// which matches on the full (type, key) pair because a catalogue does not necessarily publish a
    /// single source type.
    /// </summary>
    Task<IReadOnlyList<KnowledgeSource>> ReadAllAsync(CancellationToken cancellationToken = default);

    void Add(KnowledgeSource source);

    Task<KnowledgeSourceCounts> CountByStatusAsync(CancellationToken cancellationToken = default);
}

public sealed record KnowledgeSourceCounts(int Total, int Pending, int Ingested, int Failed, int Skipped);
