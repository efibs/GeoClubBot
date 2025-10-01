using UseCases.InputPorts.Excuses;
using UseCases.OutputPorts;

namespace UseCases.UseCases.Excuses;

public class RemoveExcuseUseCase(IExcusesRepository excusesRepository) : IRemoveExcuseUseCase
{
    public async Task<bool> RemoveExcuseAsync(Guid excuseId)
    {
        return await excusesRepository.DeleteExcuseByIdAsync(excuseId).ConfigureAwait(false);
    }
}