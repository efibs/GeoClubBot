using UseCases.InputPorts;
using UseCases.OutputPorts;

namespace UseCases;

public class ReadMemberNumStrikesUseCase(IActivityRepository activityRepository) : IReadMemberNumStrikesUseCase
{
    public async Task<int?> ReadMemberNumStrikesAsync(string memberNickname)
    {
        // Read the statuses
        var statuses = await activityRepository.ReadActivityStatusesAsync();
        
        // Find the player
        var playerStatus = statuses.Values.FirstOrDefault(s => s.Nickname == memberNickname);

        return playerStatus?.NumStrikes;
    }
}