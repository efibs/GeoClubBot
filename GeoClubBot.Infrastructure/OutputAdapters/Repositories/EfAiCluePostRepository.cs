using Entities;
using Infrastructure.OutputAdapters.DataAccess;
using Microsoft.EntityFrameworkCore;
using UseCases.OutputPorts.Repositories;

namespace Infrastructure.OutputAdapters.Repositories;

public class EfAiCluePostRepository(GeoClubBotDbContext dbContext) : IAiCluePostRepository
{
    public async Task<HashSet<Guid>> ReadChunkIdsPostedSinceAsync(DateTimeOffset sinceUtc, CancellationToken cancellationToken = default)
    {
        var ids = await dbContext.AiCluePosts
            .AsNoTracking()
            .Where(post => post.PostedAtUtc >= sinceUtc)
            .Select(post => post.ChunkId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return ids.ToHashSet();
    }

    public void Add(AiCluePost post)
    {
        dbContext.AiCluePosts.Add(post);
    }

    public async Task<IReadOnlyList<AiCluePost>> ReadLatestAsync(int count, CancellationToken cancellationToken = default) =>
        await dbContext.AiCluePosts
            .AsNoTracking()
            .OrderByDescending(post => post.PostedAtUtc)
            .Take(Math.Max(1, count))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
}
