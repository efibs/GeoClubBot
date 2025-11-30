using Entities;
using UseCases.InputPorts.Excuses;
using UseCases.OutputPorts;

namespace UseCases.UseCases.Excuses;

public class UpdateExcuseUseCase(IUnitOfWork unitOfWork) : IUpdateExcuseUseCase
{
    public async Task<ClubMemberExcuse?> UpdateExcuseAsync(Guid excuseId, DateTimeOffset from, DateTimeOffset to)
    {
        // Update the excuse
        var updatedExcuse = await unitOfWork.Excuses.UpdateExcuseAsync(excuseId, from, to).ConfigureAwait(false);
        
        // Save changes
        await unitOfWork.SaveChangesAsync().ConfigureAwait(false);
        
        return updatedExcuse;
    }
}