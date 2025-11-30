using UseCases.InputPorts.GeoGuessrAccountLinking;
using UseCases.OutputPorts;

namespace UseCases.UseCases.GeoGuessrAccountLinking;

public class GetLinkedDiscordUserIdUseCase(IUnitOfWork unitOfWork) : IGetLinkedDiscordUserIdUseCase
{
    public async Task<ulong?> GetLinkedDiscordUserIdAsync(string geoGuessrUserId)
    {
        // Read the GeoGuessr user
        var geoGuessrUser = await unitOfWork.GeoGuessrUsers.ReadUserByUserIdAsync(geoGuessrUserId).ConfigureAwait(false);

        return geoGuessrUser?.DiscordUserId;
    }
}