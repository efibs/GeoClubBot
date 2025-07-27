using UseCases.InputPorts;
using UseCases.OutputPorts;

namespace UseCases;

public class UnrevokeStrikeUseCase(IStrikesRepository strikesRepository) : IUnrevokeStrikeUseCase
{
    public async Task<bool> UnrevokeStrikeAsync(Guid strikeId)
    {
        // Delegate to repository
        return await strikesRepository.UnrevokeStrikeByIdAsync(strikeId);
    }
}