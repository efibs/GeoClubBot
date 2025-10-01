using UseCases.InputPorts.Strikes;
using UseCases.OutputPorts;

namespace UseCases.UseCases.Strikes;

public class RevokeStrikeUseCase(IStrikesRepository strikesRepository) : IRevokeStrikeUseCase
{
    public async Task<bool> RevokeStrikeAsync(Guid strikeId)
    {
        // Delegate to repository
        return await strikesRepository.RevokeStrikeByIdAsync(strikeId).ConfigureAwait(false);
    }
}