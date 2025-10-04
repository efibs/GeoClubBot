using Entities;

namespace UseCases.InputPorts.Users;

public interface IReadOrSyncGeoGuessrUserUseCase
{
    Task<GeoGuessrUser?> ReadOrSyncGeoGuessrUserByUserIdAsync(string userId);
}