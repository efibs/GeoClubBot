using Entities;
using UseCases.InputPorts.Strikes;
using UseCases.OutputPorts;

namespace UseCases.UseCases.Strikes;

public class UnrevokeStrikeUseCase(IUnitOfWork unitOfWork) : IUnrevokeStrikeUseCase
{
    public async Task<ClubMemberStrike?> UnrevokeStrikeAsync(Guid strikeId)
    {
        var strike = await unitOfWork.Strikes.UnrevokeStrikeByIdAsync(strikeId).ConfigureAwait(false);

        if (strike == null)
        {
            return null;
        }

        await unitOfWork.SaveChangesAsync().ConfigureAwait(false);

        return strike;
    }
}