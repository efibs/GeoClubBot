using UseCases.InputPorts;
using UseCases.OutputPorts;

namespace UseCases;

public class IsPlayerTrackedUseCase(IActivityRepository activityRepository) : IIsPlayerTrackedUseCase
{
    public async Task<bool> IsPlayerTrackedAsync(string memberNickname)
    {
        // Read the latest activity entries
        var latestActivityEntries = await activityRepository.ReadLatestActivityEntriesAsync();
        
        // Check if the player has an entry
        var playerIsTracked = latestActivityEntries.Values.Any(e => e.Nickname == memberNickname);
        
        return playerIsTracked;
    }
}