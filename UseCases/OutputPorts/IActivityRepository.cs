using Entities;

namespace UseCases.OutputPorts;

public interface IActivityRepository
{
    Task WriteActivityEntriesAsync(Dictionary<string, GeoGuessrClubMemberActivityEntry> entries);

    Task<Dictionary<string, GeoGuessrClubMemberActivityEntry>> ReadLatestActivityEntriesAsync();

    Task WriteMemberStatusesAsync(Dictionary<string, GeoGuessrClubMemberActivityStatus> statuses);

    Task<Dictionary<string, GeoGuessrClubMemberActivityStatus>> ReadActivityStatusesAsync();
}