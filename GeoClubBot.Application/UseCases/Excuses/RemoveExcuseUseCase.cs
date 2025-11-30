using UseCases.InputPorts.Excuses;
using UseCases.OutputPorts;

namespace UseCases.UseCases.Excuses;

public class RemoveExcuseUseCase(IUnitOfWork unitOfWork) : IRemoveExcuseUseCase
{
    public async Task<bool> RemoveExcuseAsync(Guid excuseId)
    {
        // Try to read the excuse
        var excuse = await unitOfWork.Excuses.ReadExcuseAsync(excuseId).ConfigureAwait(false);
        
        // If the excuse was not found
        if (excuse is null)
        {
            return false;
        }

        // Remove the excuse
        unitOfWork.Excuses.DeleteExcuse(excuse);
            
        // Save the changes
        await unitOfWork.SaveChangesAsync().ConfigureAwait(false);
        
        return true;
    }
}