using System.Runtime.CompilerServices;
using Entities;
using Infrastructure.OutputAdapters.DataAccess;
using Microsoft.EntityFrameworkCore;
using UseCases.OutputPorts.Repositories;

namespace Infrastructure.OutputAdapters.Repositories;

public class EfKnowledgeSourceRepository(GeoClubBotDbContext dbContext) : IKnowledgeSourceRepository
{
    /// <summary>
    /// Fewest candidates read per round trip while looking for due sources. Most of the library is
    /// indexed and not due on any given night, so a page much smaller than this is mostly wasted reads.
    /// </summary>
    private const int MinCandidatePageSize = 100;

    public async Task<KnowledgeSource?> ReadAsync(
        string sourceType,
        string naturalKey,
        CancellationToken cancellationToken = default) =>
        await dbContext.Set<KnowledgeSource>()
            .FirstOrDefaultAsync(
                source => source.SourceType == sourceType && source.NaturalKey == naturalKey,
                cancellationToken)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<KnowledgeSource>> ReadDueForIngestAsync(
        DateTimeOffset nowUtc,
        KnowledgeSourceSchedule schedule,
        int limit,
        int maxRetries,
        string? sourceType = null,
        bool ignoreSchedule = false,
        CancellationToken cancellationToken = default)
    {
        var picked = new List<KnowledgeSource>(Math.Max(0, limit));

        var due = ReadDueAsync(
            nowUtc, schedule, maxRetries, sourceType, ignoreSchedule,
            Math.Max(limit * 4, MinCandidatePageSize), cancellationToken);

        await foreach (var source in due.ConfigureAwait(false))
        {
            picked.Add(source);
            if (picked.Count >= limit)
            {
                break;
            }
        }

        return picked;
    }

    public IAsyncEnumerable<KnowledgeSource> StreamDueForIngestAsync(
        DateTimeOffset nowUtc,
        KnowledgeSourceSchedule schedule,
        int maxRetries,
        string? sourceType = null,
        bool ignoreSchedule = false,
        CancellationToken cancellationToken = default) =>
        ReadDueAsync(nowUtc, schedule, maxRetries, sourceType, ignoreSchedule, MinCandidatePageSize, cancellationToken);

    /// <summary>
    /// The due queue, in order, a page of candidates at a time. Each page is read whole before any of it
    /// is handed out, so the caller is free to use the context between sources.
    /// </summary>
    private async IAsyncEnumerable<KnowledgeSource> ReadDueAsync(
        DateTimeOffset nowUtc,
        KnowledgeSourceSchedule schedule,
        int maxRetries,
        string? sourceType,
        bool ignoreSchedule,
        int pageSize,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var candidates = dbContext.Set<KnowledgeSource>()
            .Where(source => source.Enabled
                             && source.Status != KnowledgeSourceStatus.Skipped
                             && source.RemovedFromSyncAtUtc == null);

        if (sourceType is not null)
        {
            var type = sourceType.ToLowerInvariant();
            candidates = candidates.Where(source => source.SourceType.ToLower() == type);
        }

        // The queue's order, as far as SQL can express it: never indexed, owed pictures, a re-index, and
        // last whatever failed — the same split the domain draws with IsRetrying. Within each, the
        // longest-waiting first; among the failing, the fewest failures first, so a source that has
        // failed once is not starved by a backlog of dead links.
        var ordered = candidates
            .OrderBy(source => source.ConsecutiveFailures > 0 ? 3
                : source.LastIngestedAtUtc == null ? 0
                : source.ImagesDeferred ? 1
                : 2)
            .ThenBy(source => source.ConsecutiveFailures)
            .ThenBy(source => source.LastAttemptedAtUtc ?? DateTimeOffset.MinValue)
            .ThenBy(source => source.SourceId);

        // Paged until the caller has enough, with the exact due test applied in memory: the schedule is
        // domain behaviour, and duplicating it as a translatable expression would let the two drift
        // apart. A single over-fetched window is what hid due sources behind a hundred older ones that
        // were not due, leaving a run with less to do than was waiting.
        var retries = 0;

        for (var offset = 0; ; offset += pageSize)
        {
            var page = await ordered
                .Skip(offset)
                .Take(pageSize)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            foreach (var source in page)
            {
                if (!ignoreSchedule && !source.IsDueForIngest(nowUtc, schedule))
                {
                    continue;
                }

                if (source.IsRetrying)
                {
                    // Failing sources sort last, so once their share is taken nothing later can be picked.
                    if (retries >= maxRetries)
                    {
                        yield break;
                    }

                    retries++;
                }

                yield return source;
            }

            if (page.Count < pageSize)
            {
                yield break;
            }
        }
    }

    public async Task<IReadOnlyList<KnowledgeSource>> ReadRemovedWithIndexedContentAsync(
        DateTimeOffset removedBeforeUtc,
        string? sourceType = null,
        CancellationToken cancellationToken = default)
    {
        var removed = dbContext.Set<KnowledgeSource>()
            .Where(source => source.RemovedFromSyncAtUtc != null
                             && source.RemovedFromSyncAtUtc <= removedBeforeUtc
                             && (source.ContentHash != null || source.ChunkCount > 0 || source.ImageCount > 0));

        if (sourceType is not null)
        {
            var type = sourceType.ToLowerInvariant();
            removed = removed.Where(source => source.SourceType.ToLower() == type);
        }

        return await removed
            .OrderBy(source => source.RemovedFromSyncAtUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<KnowledgeSource>> ReadMatchingAsync(
        string? keyOrUrl,
        string? sourceType,
        CancellationToken cancellationToken = default)
    {
        var key = string.IsNullOrWhiteSpace(keyOrUrl) ? null : keyOrUrl.Trim();
        var type = string.IsNullOrWhiteSpace(sourceType) ? null : sourceType.Trim().ToLowerInvariant();

        if (key is null && type is null)
        {
            return [];
        }

        var query = dbContext.Set<KnowledgeSource>()
            .Where(source => source.RemovedFromSyncAtUtc == null);

        if (key is not null)
        {
            query = query.Where(source => source.NaturalKey == key || source.Url == key);
        }

        if (type is not null)
        {
            query = query.Where(source => source.SourceType.ToLower() == type);
        }

        return await query
            .OrderBy(source => source.SourceType)
            .ThenBy(source => source.NaturalKey)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<KnowledgeSourcePage> ListAsync(
        KnowledgeSourceListFilter filter,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.Set<KnowledgeSource>().AsNoTracking();

        if (filter.RemovedFromSync)
        {
            query = query.Where(source => source.RemovedFromSyncAtUtc != null);
        }
        else
        {
            var statuses = filter.Statuses.ToList();
            query = query.Where(source => source.RemovedFromSyncAtUtc == null && statuses.Contains(source.Status));
        }

        if (filter.SourceType is { } sourceType)
        {
            var type = sourceType.ToLowerInvariant();
            query = query.Where(source => source.SourceType.ToLower() == type);
        }

        var total = await query.CountAsync(cancellationToken).ConfigureAwait(false);

        var sources = await query
            .OrderBy(source => source.Status)
            .ThenBy(source => source.SourceType)
            .ThenBy(source => source.Title ?? source.NaturalKey)
            .ThenBy(source => source.NaturalKey)
            .Take(Math.Max(0, limit))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new KnowledgeSourcePage(sources, total);
    }

    /// <summary>
    /// Deliberately tracked, unlike the other reads: the catalogue sync updates and tombstones the
    /// sources it returns, and changes to a detached entity are silently never saved.
    /// </summary>
    public async Task<IReadOnlyList<KnowledgeSource>> ReadAllAsync(CancellationToken cancellationToken = default) =>
        await dbContext.Set<KnowledgeSource>()
            .OrderBy(source => source.SourceType)
            .ThenBy(source => source.NaturalKey)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public void Add(KnowledgeSource source) => dbContext.Set<KnowledgeSource>().Add(source);

    public async Task<KnowledgeSourceCounts> CountByStatusAsync(CancellationToken cancellationToken = default)
    {
        var counts = await dbContext.Set<KnowledgeSource>()
            .AsNoTracking()
            .GroupBy(source => new { source.Status, Removed = source.RemovedFromSyncAtUtc != null })
            .Select(group => new { group.Key.Status, group.Key.Removed, Count = group.Count() })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // A source no catalogue lists any more is counted once, as removed, whatever its last status:
        // counted as indexed too, it would go on inflating that figure after its chunks were swept.
        int For(KnowledgeSourceStatus status) =>
            counts.Where(entry => !entry.Removed && entry.Status == status).Sum(entry => entry.Count);

        return new KnowledgeSourceCounts(
            counts.Sum(entry => entry.Count),
            For(KnowledgeSourceStatus.Pending),
            For(KnowledgeSourceStatus.Ingested),
            For(KnowledgeSourceStatus.Failed),
            For(KnowledgeSourceStatus.Skipped),
            For(KnowledgeSourceStatus.Unavailable),
            counts.Where(entry => entry.Removed).Sum(entry => entry.Count));
    }
}
