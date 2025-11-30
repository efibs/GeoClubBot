using UseCases.InputPorts.Strikes;
using UseCases.OutputPorts;

namespace UseCases.UseCases.Strikes;

public class RevokeStrikeUseCase(IUnitOfWork unitOfWork) : IRevokeStrikeUseCase
{
    public async Task<bool> RevokeStrikeAsync(Guid strikeId)
    {
        // Delegate to repository
        var successful = await unitOfWork.Strikes.RevokeStrikeByIdAsync(strikeId).ConfigureAwait(false);
        
        // If the update was not successful
        if (successful == false)
        {
            return false;
        }
        
        // Save the changes
        await unitOfWork.SaveChangesAsync().ConfigureAwait(false);
        
        return true;
    }
}