using Entities;
using Infrastructure.OutputAdapters.DataAccess;
using Microsoft.EntityFrameworkCore;
using UseCases.OutputPorts.Repositories;

namespace Infrastructure.OutputAdapters.Repositories;

public class EfKnowledgeSourceRepository(GeoClubBotDbContext dbContext) : IKnowledgeSourceRepository
{
    public async Task<KnowledgeSource?> ReadAsync(
        string sourceType,
        string naturalKey,
        CancellationToken cancellationToken = default) =>
        await dbContext.Set<KnowledgeSource>()
            .FirstOrDefaultAsync(
                source => source.SourceType == sourceType && source.NaturalKey == naturalKey,
                cancellationToken)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<KnowledgeSource>> ReadByTypeAsync(
        string sourceType,
        CancellationToken cancellationToken = default) =>
        await dbContext.Set<KnowledgeSource>()
            .Where(source => source.SourceType == sourceType)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<KnowledgeSource>> ReadDueForIngestAsync(
        DateTimeOffset nowUtc,
        TimeSpan reingestInterval,
        int limit,
        bool ignoreSchedule = false,
        CancellationToken cancellationToken = default)
    {
        // Candidates are narrowed in SQL and the exact due test is applied in memory: the backoff
        // curve is domain behaviour, and duplicating it as a translatable expression would let the
        // two drift apart.
        var candidates = await dbContext.Set<KnowledgeSource>()
            .Where(source => source.Enabled
                             && source.Status != KnowledgeSourceStatus.Skipped
                             && source.RemovedFromSyncAtUtc == null)
            .OrderBy(source => source.LastAttemptedAtUtc ?? DateTimeOffset.MinValue)
            // Over-fetch so in-memory filtering still has enough to fill the run's budget.
            .Take(limit * 4)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return ignoreSchedule
            ? [.. candidates.Take(limit)]
            : [.. candidates.Where(source => source.IsDueForIngest(nowUtc, reingestInterval)).Take(limit)];
    }

    public async Task<IReadOnlyList<KnowledgeSource>> ReadAllAsync(CancellationToken cancellationToken = default) =>
        await dbContext.Set<KnowledgeSource>()
            .AsNoTracking()
            .OrderBy(source => source.SourceType)
            .ThenBy(source => source.NaturalKey)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public void Add(KnowledgeSource source) => dbContext.Set<KnowledgeSource>().Add(source);

    public async Task<KnowledgeSourceCounts> CountByStatusAsync(CancellationToken cancellationToken = default)
    {
        var counts = await dbContext.Set<KnowledgeSource>()
            .AsNoTracking()
            .GroupBy(source => source.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        int For(KnowledgeSourceStatus status) =>
            counts.FirstOrDefault(entry => entry.Status == status)?.Count ?? 0;

        return new KnowledgeSourceCounts(
            counts.Sum(entry => entry.Count),
            For(KnowledgeSourceStatus.Pending),
            For(KnowledgeSourceStatus.Ingested),
            For(KnowledgeSourceStatus.Failed),
            For(KnowledgeSourceStatus.Skipped));
    }
}
