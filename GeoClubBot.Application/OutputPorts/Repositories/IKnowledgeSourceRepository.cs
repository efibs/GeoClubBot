using Entities;

namespace UseCases.OutputPorts.Repositories;

public interface IKnowledgeSourceRepository
{
    Task<KnowledgeSource?> ReadAsync(string sourceType, string naturalKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// The sources a run should attempt, in the order it should attempt them: never indexed first, then
    /// those owed pictures, then re-indexes that have come due, and last those whose previous attempt
    /// went wrong — fewest failures first, and no more than <paramref name="maxRetries"/> of them.
    ///
    /// The order is what keeps the queue fair. Oldest attempt first let ten dead links that shared a
    /// timestamp take ten of a run's twenty-five slots together, and a source that was due could sit
    /// unseen behind a hundred older ones that were not.
    /// </summary>
    /// <param name="sourceType">
    /// Restricts the queue to one extractor family before the limit is applied, so a run for one family
    /// is not handed the first <paramref name="limit"/> sources of every family and left with a few.
    /// </param>
    /// <param name="ignoreSchedule">
    /// Bypasses the re-ingest interval and failure backoff, for an operator-triggered run. Without it
    /// a manual "force" does nothing at all on sources that were indexed recently.
    /// </param>
    Task<IReadOnlyList<KnowledgeSource>> ReadDueForIngestAsync(
        DateTimeOffset nowUtc,
        KnowledgeSourceSchedule schedule,
        int limit,
        int maxRetries,
        string? sourceType = null,
        bool ignoreSchedule = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The same queue as <see cref="ReadDueForIngestAsync"/>, in the same order and under the same cap on
    /// failing sources, read a page at a time for as long as the caller keeps taking.
    ///
    /// For a run that may pass sources over: one whose host has refused it leaves its sources where they
    /// are and fills their slots from further down the queue. Handed a fixed batch instead, a run whose
    /// batch was all one refusing host's sources did nothing else, night after night, while the refusal
    /// lasted — every other site's due guides waiting behind them.
    /// </summary>
    IAsyncEnumerable<KnowledgeSource> StreamDueForIngestAsync(
        DateTimeOffset nowUtc,
        KnowledgeSourceSchedule schedule,
        int maxRetries,
        string? sourceType = null,
        bool ignoreSchedule = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sources no catalogue has listed since before <paramref name="removedBeforeUtc"/> whose chunks may
    /// still be in the index, tracked so the caller can record their removal.
    /// </summary>
    Task<IReadOnlyList<KnowledgeSource>> ReadRemovedWithIndexedContentAsync(
        DateTimeOffset removedBeforeUtc,
        string? sourceType = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sources a catalogue still lists that match an admin's description, tracked so a change to them is
    /// persisted. At least one of the two should be given; with neither, nothing matches.
    /// </summary>
    /// <param name="keyOrUrl">
    /// A source's natural key, in any family, or its full link — whichever the admin has to hand, since
    /// a listing shows both.
    /// </param>
    /// <param name="sourceType">Restricts the match to one extractor family, or names the whole family.</param>
    Task<IReadOnlyList<KnowledgeSource>> ReadMatchingAsync(
        string? keyOrUrl,
        string? sourceType,
        CancellationToken cancellationToken = default);

    /// <summary>A page of sources for an admin to read, and how many matched in all. Not tracked.</summary>
    Task<KnowledgeSourcePage> ListAsync(
        KnowledgeSourceListFilter filter,
        int limit,
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

/// <summary>
/// Sources by state. The status counts cover sources a catalogue still lists; <paramref name="Removed"/>
/// counts the ones none does any more, whatever their status, and <paramref name="Total"/> is both.
/// </summary>
public sealed record KnowledgeSourceCounts(
    int Total,
    int Pending,
    int Ingested,
    int Failed,
    int Skipped,
    int Unavailable = 0,
    int Removed = 0);

/// <param name="Statuses">The statuses listed. Ignored when <paramref name="RemovedFromSync"/> is set.</param>
/// <param name="RemovedFromSync">Lists the sources no catalogue lists any more, instead of the live ones.</param>
/// <param name="SourceType">Restricts the listing to one extractor family.</param>
public sealed record KnowledgeSourceListFilter(
    IReadOnlyCollection<KnowledgeSourceStatus> Statuses,
    bool RemovedFromSync = false,
    string? SourceType = null);

/// <param name="Total">How many sources matched, of which <paramref name="Sources"/> is the first page.</param>
public sealed record KnowledgeSourcePage(IReadOnlyList<KnowledgeSource> Sources, int Total);
