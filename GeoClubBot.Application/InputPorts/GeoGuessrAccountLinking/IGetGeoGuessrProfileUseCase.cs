using UseCases.OutputPorts.GeoGuessr;

namespace UseCases.InputPorts.GeoGuessrAccountLinking;

public interface IGetGeoGuessrProfileUseCase
{
    Task<UserDto?> GetGeoGuessrProfileAsync(ulong discordUserId);
}
