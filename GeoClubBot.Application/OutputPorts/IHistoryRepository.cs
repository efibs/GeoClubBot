using Entities;

namespace UseCases.OutputPorts;

public interface IHistoryRepository
{
    Task<bool> CreateHistoryEntriesAsync(IEnumerable<ClubMemberHistoryEntry> entries);

    Task<List<ClubMemberHistoryEntry>> ReadHistoryEntriesAsync();

    Task<int> DeleteHistoryEntriesBeforeAsync(DateTimeOffset threshold);
}