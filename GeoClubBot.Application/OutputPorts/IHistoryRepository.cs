using Entities;
using UseCases.OutputPorts.Projections;

namespace UseCases.OutputPorts;

public interface IHistoryRepository
{
    List<ClubMemberHistoryEntry> CreateHistoryEntries(ICollection<ClubMemberHistoryEntry> entries);

    Task<List<ClubMemberHistoryEntry>> ReadHistoryEntriesAsync(Guid clubId, CancellationToken cancellationToken = default);

    Task<List<ClubMemberHistoryEntry>?> ReadHistoryEntriesByPlayerNicknameAsync(string playerNickname, Guid clubId, CancellationToken cancellationToken = default);

    Task<List<LatestHistoryEntryProjection>> ReadLatestHistoryEntryProjectionsByClubIdAsync(Guid clubId, CancellationToken cancellationToken = default);

    Task<List<ClubMemberHistoryEntry>> ReadHistoryEntriesByClubIdAsync(Guid clubId, CancellationToken cancellationToken = default);

    Task<List<HistoryEntryProjection>> ReadHistoryEntryProjectionsByClubIdAsync(Guid clubId, CancellationToken cancellationToken = default);

    Task<int> DeleteHistoryEntriesBeforeAsync(DateTimeOffset threshold, CancellationToken cancellationToken = default);
}
