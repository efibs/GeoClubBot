using Entities;
using UseCases.InputPorts.GeoGuessrAccountLinking;
using UseCases.OutputPorts;

namespace UseCases.UseCases.GeoGuessrAccountLinking;

public class GetLinkedGeoGuessrUserUseCase(IUnitOfWork unitOfWork) : IGetLinkedGeoGuessrUserUseCase
{
    public async Task<GeoGuessrUser?> GetLinkedGeoGuessrUserAsync(ulong discordUserId)
    {
        return await unitOfWork.GeoGuessrUsers.ReadUserByDiscordUserIdAsync(discordUserId).ConfigureAwait(false);
    }
}