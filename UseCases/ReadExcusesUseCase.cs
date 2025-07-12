using Entities;
using UseCases.InputPorts;
using UseCases.OutputPorts;

namespace UseCases;

public class ReadExcusesUseCase(IExcusesRepository excusesRepository) : IReadExcusesUseCase
{
    public async Task<List<GeoGuessrClubMemberExcuse>> ReadExcusesAsync(string memberNickname)
    {
        // Read the excuses
        var allExcuses = await excusesRepository.ReadExcusesAsync();
        
        // Try to get the excuses of the player
        allExcuses.TryGetValue(memberNickname, out var memberExcuses);
        
        return memberExcuses ?? [];
    }
}