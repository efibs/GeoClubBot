using Entities;
using Infrastructure.OutputAdapters.DataAccess;
using Microsoft.EntityFrameworkCore;
using UseCases.OutputPorts.Repositories;

namespace Infrastructure.OutputAdapters.Repositories;

public class EfAiRetrievalGapRepository(GeoClubBotDbContext dbContext) : IAiRetrievalGapRepository
{
    public void Add(AiRetrievalGap gap) => dbContext.AiRetrievalGaps.Add(gap);

    public async Task<IReadOnlyList<AiRetrievalGap>> ReadSinceAsync(DateTimeOffset sinceUtc, CancellationToken cancellationToken = default) =>
        await dbContext.AiRetrievalGaps
            .AsNoTracking()
            .Where(gap => gap.AskedAtUtc >= sinceUtc)
            .OrderByDescending(gap => gap.AskedAtUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public Task<int> DeleteOlderThanAsync(DateTimeOffset cutoffUtc, CancellationToken cancellationToken = default) =>
        dbContext.AiRetrievalGaps.Where(gap => gap.AskedAtUtc < cutoffUtc).ExecuteDeleteAsync(cancellationToken);
}
