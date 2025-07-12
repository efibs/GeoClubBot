using Entities;
using UseCases.InputPorts;
using UseCases.OutputPorts;

namespace UseCases;

public class WriteMemberNumStrikesUseCase(IActivityRepository activityRepository) : IWriteMemberNumStrikesUseCase
{
    public async Task<bool> WriteNumStrikesAsync(string memberNickname, int numStrikes)
    {
        // Read the statuses
        var statuses = await activityRepository.ReadActivityStatusesAsync();

        // If the player is not being tracked
        if (statuses.Values.All(s => s.Nickname != memberNickname))
        {
            return false;
        }

        // Find the player
        var playerStatus = statuses
            .First(s => s.Value.Nickname == memberNickname);


        // Update the player status
        var newPlayerStatus = playerStatus.Value with { NumStrikes = numStrikes };

        // Create a dictionary with the single entry
        var statusesDict = new Dictionary<string, GeoGuessrClubMemberActivityStatus>
        {
            { playerStatus.Key, newPlayerStatus }
        };

        // Save the player status
        await activityRepository.WriteMemberStatusesAsync(statusesDict);

        return true;
    }
}