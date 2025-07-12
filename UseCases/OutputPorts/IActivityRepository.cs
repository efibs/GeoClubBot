using Entities;

namespace UseCases.OutputPorts;

public interface IActivityRepository
{
    Task WriteActivityEntriesAsync(Dictionary<Guid, GeoGuessrClubMemberActivityEntry> entries);
    
    Task<Dictionary<Guid, GeoGuessrClubMemberActivityEntry>> ReadLatestActivityEntriesAsync();

    Task WriteMemberStatusesAsync(Dictionary<Guid, GeoGuessrClubMemberActivityStatus> statuses);
    
    Task<Dictionary<Guid, GeoGuessrClubMemberActivityStatus>> ReadActivityStatusesAsync();
}