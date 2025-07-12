using UseCases.InputPorts;
using UseCases.OutputPorts;

namespace UseCases;

public class RemoveExcuseUseCase(IExcusesRepository excusesRepository) : IRemoveExcuseUseCase
{
    public async Task<bool> RemoveExcuseAsync(Guid excuseId)
    {
        return await excusesRepository.DeleteExcuseAsync(excuseId);
    }
}