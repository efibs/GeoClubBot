using UseCases.InputPorts.GeoGuessrAccountLinking;
using UseCases.OutputPorts;

namespace UseCases.UseCases.GeoGuessrAccountLinking;

public class GetLinkedDiscordUserIdUseCase(IGeoGuessrUserRepository geoGuessrUserRepository) : IGetLinkedDiscordUserIdUseCase
{
    public async Task<ulong?> GetLinkedDiscordUserIdAsync(string geoGuessrUserId)
    {
        // Read the GeoGuessr user
        var geoGuessrUser = await geoGuessrUserRepository.ReadUserByUserIdAsync(geoGuessrUserId).ConfigureAwait(false);

        return geoGuessrUser?.DiscordUserId;
    }
}