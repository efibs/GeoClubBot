using Entities;

namespace UseCases.OutputPorts.Repositories;

public interface IAiRetrievalGapRepository
{
    void Add(AiRetrievalGap gap);

    /// <summary>The gaps recorded since <paramref name="sinceUtc"/>, newest first.</summary>
    Task<IReadOnlyList<AiRetrievalGap>> ReadSinceAsync(DateTimeOffset sinceUtc, CancellationToken cancellationToken = default);

    /// <summary>Deletes gaps recorded before <paramref name="cutoffUtc"/>; returns how many.</summary>
    Task<int> DeleteOlderThanAsync(DateTimeOffset cutoffUtc, CancellationToken cancellationToken = default);
}
