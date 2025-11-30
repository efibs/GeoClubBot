using Entities;

namespace UseCases.OutputPorts;

public interface IHistoryRepository
{
    List<ClubMemberHistoryEntry> CreateHistoryEntries(ICollection<ClubMemberHistoryEntry> entries);

    Task<List<ClubMemberHistoryEntry>> ReadHistoryEntriesAsync();
    
    Task<List<ClubMemberHistoryEntry>?> ReadHistoryEntriesByPlayerNicknameAsync(string playerNickname);
    
    Task<List<ClubMemberHistoryEntry>> ReadLatestHistoryEntriesAsync();

    Task<int> DeleteHistoryEntriesBeforeAsync(DateTimeOffset threshold);
}