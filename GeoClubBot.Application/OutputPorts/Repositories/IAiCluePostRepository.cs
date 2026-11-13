using Entities;

namespace UseCases.OutputPorts.Repositories;

public interface IAiCluePostRepository
{
    /// <summary>The chunks posted as a clue of the day since <paramref name="sinceUtc"/>, which are not posted again yet.</summary>
    Task<HashSet<Guid>> ReadChunkIdsPostedSinceAsync(DateTimeOffset sinceUtc, CancellationToken cancellationToken = default);

    void Add(AiCluePost post);

    /// <summary>The latest posts, newest first, for the admin's overview.</summary>
    Task<IReadOnlyList<AiCluePost>> ReadLatestAsync(int count, CancellationToken cancellationToken = default);
}
