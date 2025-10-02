using Entities;
using UseCases.InputPorts.GeoGuessrAccountLinking;
using UseCases.OutputPorts;

namespace UseCases.UseCases.GeoGuessrAccountLinking;

public class GetLinkedGeoGuessrUserUseCase(IGeoGuessrUserRepository repository) : IGetLinkedGeoGuessrUserUseCase
{
    public async Task<GeoGuessrUser?> GetLinkedGeoGuessrUserAsync(ulong discordUserId)
    {
        return await repository.ReadUserByDiscordUserIdAsync(discordUserId).ConfigureAwait(false);
    }
}