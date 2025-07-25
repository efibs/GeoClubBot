using Entities;
using UseCases.OutputPorts;

namespace Infrastructure.OutputAdapters;

public class EfHistoryRepository : IHistoryRepository
{
    public Task<bool> CreateHistoryEntriesAsync(IEnumerable<ClubMemberHistoryEntry> entries)
    {
        throw new NotImplementedException();
    }

    public Task<List<ClubMemberHistoryEntry>> ReadHistoryEntriesAsync()
    {
        throw new NotImplementedException();
    }

    public Task<int> DeleteHistoryEntriesBeforeAsync(DateTimeOffset threshold)
    {
        throw new NotImplementedException();
    }
}