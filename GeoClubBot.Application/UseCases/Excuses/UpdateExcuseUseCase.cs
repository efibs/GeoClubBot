using Entities;
using UseCases.InputPorts.Excuses;
using UseCases.OutputPorts;

namespace UseCases.UseCases.Excuses;

public class UpdateExcuseUseCase(IExcusesRepository excusesRepository) : IUpdateExcuseUseCase
{
    public async Task<ClubMemberExcuse?> UpdateExcuseAsync(Guid excuseId, DateTimeOffset from, DateTimeOffset to)
    {
        return await excusesRepository.UpdateExcuseAsync(excuseId, from, to).ConfigureAwait(false);
    }
}