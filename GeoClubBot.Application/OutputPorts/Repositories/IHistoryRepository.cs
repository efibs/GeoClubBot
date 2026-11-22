using Entities;
using UseCases.OutputPorts.Projections;

namespace UseCases.OutputPorts.Repositories;

public interface IHistoryRepository
{
    List<ClubMemberHistoryEntry> CreateHistoryEntries(ICollection<ClubMemberHistoryEntry> entries);

    Task<List<ClubMemberHistoryEntry>> ReadHistoryEntriesAsync(Guid clubId, CancellationToken cancellationToken = default);

    /// <summary>The player's history entries in that club, in no particular order.</summary>
    Task<List<ClubMemberHistoryEntry>> ReadHistoryEntriesByUserIdAsync(string userId, Guid clubId, CancellationToken cancellationToken = default);

    Task<List<LatestHistoryEntryProjection>> ReadLatestHistoryEntryProjectionsByClubIdAsync(Guid clubId, CancellationToken cancellationToken = default);

    Task<List<ClubMemberHistoryEntry>> ReadHistoryEntriesByClubIdAsync(Guid clubId, CancellationToken cancellationToken = default);

    Task<List<HistoryEntryProjection>> ReadHistoryEntryProjectionsByClubIdAsync(Guid clubId, CancellationToken cancellationToken = default);

    Task<int> DeleteHistoryEntriesBeforeAsync(DateTimeOffset threshold, CancellationToken cancellationToken = default);
}
