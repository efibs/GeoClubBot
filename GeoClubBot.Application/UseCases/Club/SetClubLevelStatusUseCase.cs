using UseCases.InputPorts.Club;
using UseCases.OutputPorts;

namespace UseCases.UseCases.Club;

public class SetClubLevelStatusUseCase(IStatusUpdater statusUpdater) : ISetClubLevelStatusUseCase
{
    public async Task SetClubLevelStatusAsync(int level)
    {
        // Build the status message
        var newStatus = $"Level {level} club!";

        // Update the status
        await statusUpdater.UpdateStatusAsync(newStatus).ConfigureAwait(false);
    }
}