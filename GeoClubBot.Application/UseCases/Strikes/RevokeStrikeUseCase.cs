using Entities;
using UseCases.InputPorts.Strikes;
using UseCases.OutputPorts;

namespace UseCases.UseCases.Strikes;

public class RevokeStrikeUseCase(IUnitOfWork unitOfWork) : IRevokeStrikeUseCase
{
    public async Task<ClubMemberStrike?> RevokeStrikeAsync(Guid strikeId)
    {
        var strike = await unitOfWork.Strikes.RevokeStrikeByIdAsync(strikeId).ConfigureAwait(false);

        if (strike == null)
        {
            return null;
        }

        await unitOfWork.SaveChangesAsync().ConfigureAwait(false);

        return strike;
    }
}