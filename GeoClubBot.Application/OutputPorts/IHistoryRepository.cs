using Entities;

namespace UseCases.OutputPorts;

public interface IHistoryRepository
{
    List<ClubMemberHistoryEntry> CreateHistoryEntries(ICollection<ClubMemberHistoryEntry> entries);

    Task<List<ClubMemberHistoryEntry>> ReadHistoryEntriesAsync();
    
    Task<List<ClubMemberHistoryEntry>?> ReadHistoryEntriesByPlayerNicknameAsync(string playerNickname);
    
    Task<List<ClubMemberHistoryEntry>> ReadLatestHistoryEntriesAsync();

    Task<List<ClubMemberHistoryEntry>> ReadLatestHistoryEntriesByClubIdAsync(Guid clubId);

    Task<List<ClubMemberHistoryEntry>> ReadHistoryEntriesByClubIdAsync(Guid clubId);

    Task<int> DeleteHistoryEntriesBeforeAsync(DateTimeOffset threshold);
}