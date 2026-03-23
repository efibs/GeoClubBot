using Entities;

namespace UseCases.OutputPorts;

public interface IHistoryRepository
{
    List<ClubMemberHistoryEntry> CreateHistoryEntries(ICollection<ClubMemberHistoryEntry> entries);

    Task<List<ClubMemberHistoryEntry>> ReadHistoryEntriesAsync(Guid clubId);
    
    Task<List<ClubMemberHistoryEntry>?> ReadHistoryEntriesByPlayerNicknameAsync(string playerNickname, Guid clubId);

    Task<List<ClubMemberHistoryEntry>> ReadLatestHistoryEntriesByClubIdAsync(Guid clubId);

    Task<List<ClubMemberHistoryEntry>> ReadHistoryEntriesByClubIdAsync(Guid clubId);

    Task<int> DeleteHistoryEntriesBeforeAsync(DateTimeOffset threshold);
}