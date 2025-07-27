using UseCases.InputPorts;
using UseCases.OutputPorts;

namespace UseCases;

public class RevokeStrikeUseCase(IStrikesRepository strikesRepository) : IRevokeStrikeUseCase
{
    public async Task<bool> RevokeStrikeAsync(Guid strikeId)
    {
        // Delegate to repository
        return await strikesRepository.RevokeStrikeByIdAsync(strikeId);
    }
}