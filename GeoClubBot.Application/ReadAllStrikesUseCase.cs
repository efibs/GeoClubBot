using Entities;
using UseCases.InputPorts;
using UseCases.OutputPorts;

namespace UseCases;

public class ReadAllStrikesUseCase(IStrikesRepository strikesRepository) : IReadAllStrikesUseCase
{
    public async Task<List<ClubMemberStrike>> ReadAllStrikesAsync()
    {
        // Read the strikes
        var strikes = await strikesRepository.ReadAllStrikesAsync();
        
        return strikes;
    }
}