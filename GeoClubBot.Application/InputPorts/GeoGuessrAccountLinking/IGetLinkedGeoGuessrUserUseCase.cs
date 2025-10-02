using Entities;

namespace UseCases.InputPorts.GeoGuessrAccountLinking;

public interface IGetLinkedGeoGuessrUserUseCase
{
    Task<GeoGuessrUser?> GetLinkedGeoGuessrUserAsync(ulong discordUserId);
}