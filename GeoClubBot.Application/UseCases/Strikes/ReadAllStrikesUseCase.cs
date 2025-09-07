using Entities;
using UseCases.InputPorts.Strikes;
using UseCases.OutputPorts;

namespace UseCases.UseCases.Strikes;

public class ReadAllStrikesUseCase(IStrikesRepository strikesRepository) : IReadAllStrikesUseCase
{
    public async Task<List<ClubMemberStrike>> ReadAllStrikesAsync()
    {
        // Read the strikes
        var strikes = await strikesRepository.ReadAllStrikesAsync();
        
        return strikes;
    }
}