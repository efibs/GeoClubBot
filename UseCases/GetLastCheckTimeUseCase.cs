using UseCases.InputPorts;
using UseCases.OutputPorts;

namespace UseCases;

public class GetLastCheckTimeUseCase(IActivityRepository activityRepository) : IGetLastCheckTimeUseCase
{
    public async Task<DateTimeOffset?> GetLastCheckTimeAsync()
    {
        // Get all the latest activities
        var latestActivities = await activityRepository.ReadLatestActivityEntriesAsync();
        
        // If there are no latest activities
        if (latestActivities.Count == 0)
        {
            return null;
        }
        
        // Get the latest timestamp of all activity entries
        var lastCheckTime = latestActivities.Values
            .Select(a => a.Timestamp)
            .Max();
        
        return lastCheckTime;
    }
}