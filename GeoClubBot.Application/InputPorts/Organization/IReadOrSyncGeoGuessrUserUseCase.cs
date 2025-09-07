using Entities;

namespace UseCases.InputPorts.Organization;

public interface IReadOrSyncGeoGuessrUserUseCase
{
    Task<GeoGuessrUser?> ReadOrSyncGeoGuessrUserByUserIdAsync(string userId);
}