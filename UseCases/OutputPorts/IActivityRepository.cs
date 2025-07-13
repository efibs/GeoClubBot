using Entities;

namespace UseCases.OutputPorts;

public interface IActivityRepository
{
    Task WriteActivityEntriesAsync(Dictionary<string, GeoGuessrClubMemberActivityEntry> entries);

    Task<Dictionary<string, List<GeoGuessrClubMemberActivityEntry>>> ReadActivityHistoryAsync();
    
    Task OverwriteActivityHistoryAsync(Dictionary<string, List<GeoGuessrClubMemberActivityEntry>> entries);
    
    Task<Dictionary<string, GeoGuessrClubMemberActivityEntry>> ReadLatestActivityEntriesAsync();

    Task OverwriteLatestActivityEntriesAsync(Dictionary<string, GeoGuessrClubMemberActivityEntry> entries);
    
    Task WriteMemberStatusesAsync(Dictionary<string, GeoGuessrClubMemberActivityStatus> statuses);

    Task<Dictionary<string, GeoGuessrClubMemberActivityStatus>> ReadActivityStatusesAsync();
    
    Task OverwriteActivityStatusesAsync(Dictionary<string, GeoGuessrClubMemberActivityStatus> statuses);
}