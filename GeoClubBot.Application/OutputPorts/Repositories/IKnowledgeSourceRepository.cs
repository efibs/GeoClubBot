using Entities;

namespace UseCases.OutputPorts.Repositories;

public interface IKnowledgeSourceRepository
{
    Task<KnowledgeSource?> ReadAsync(string sourceType, string naturalKey, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<KnowledgeSource>> ReadByTypeAsync(string sourceType, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sources worth attempting now, oldest attempt first so the queue drains evenly rather than
    /// re-processing the same few every run.
    /// </summary>
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

    Task<IReadOnlyList<KnowledgeSource>> ReadAllAsync(CancellationToken cancellationToken = default);

    void Add(KnowledgeSource source);

    Task<KnowledgeSourceCounts> CountByStatusAsync(CancellationToken cancellationToken = default);
}

public sealed record KnowledgeSourceCounts(int Total, int Pending, int Ingested, int Failed, int Skipped);
