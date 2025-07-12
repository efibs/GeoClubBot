using Entities;
using UseCases.InputPorts;
using UseCases.OutputPorts;

namespace UseCases;

public class AddExcuseUseCase(IExcusesRepository excusesRepository) : IAddExcuseUseCase
{
    public async Task<Guid> AddExcuseAsync(string memberNickname, DateTimeOffset from, DateTimeOffset to)
    {
        // Build the new excuse
        var newExcuse = new GeoGuessrClubMemberExcuse(Guid.NewGuid(), from, to);
        
        // Write the excuse
        await excusesRepository.WriteExcuseAsync(memberNickname, newExcuse);
        
        return newExcuse.Id;
    }
}