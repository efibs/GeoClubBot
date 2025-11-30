using UseCases.InputPorts.Strikes;
using UseCases.OutputPorts;

namespace UseCases.UseCases.Strikes;

public class UnrevokeStrikeUseCase(IUnitOfWork unitOfWork) : IUnrevokeStrikeUseCase
{
    public async Task<bool> UnrevokeStrikeAsync(Guid strikeId)
    {
        // Delegate to repository
        var successful = await unitOfWork.Strikes.UnrevokeStrikeByIdAsync(strikeId).ConfigureAwait(false);
        
        // If the unrevoke was not successful
        if (successful == false)
        {
            return false;
        }
        
        // Save the changes
        await unitOfWork.SaveChangesAsync().ConfigureAwait(false);
        
        return true;
    }
}