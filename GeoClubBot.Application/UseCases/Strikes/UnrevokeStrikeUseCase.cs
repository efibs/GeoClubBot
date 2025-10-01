using UseCases.InputPorts.Strikes;
using UseCases.OutputPorts;

namespace UseCases.UseCases.Strikes;

public class UnrevokeStrikeUseCase(IStrikesRepository strikesRepository) : IUnrevokeStrikeUseCase
{
    public async Task<bool> UnrevokeStrikeAsync(Guid strikeId)
    {
        // Delegate to repository
        return await strikesRepository.UnrevokeStrikeByIdAsync(strikeId).ConfigureAwait(false);
    }
}