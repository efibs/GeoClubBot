using Entities;
using UseCases.InputPorts.Users;
using UseCases.OutputPorts;
using UseCases.OutputPorts.GeoGuessr;

namespace UseCases.UseCases.Users;

public class ReadOrSyncGeoGuessrUserUseCase(IGeoGuessrUserRepository geoGuessrUserRepository, 
    ICreateOrUpdateUserUseCase createOrUpdateUserUseCase,
    IGeoGuessrAccess geoGuessrAccess) : IReadOrSyncGeoGuessrUserUseCase
{
    public async Task<GeoGuessrUser?> ReadOrSyncGeoGuessrUserByUserIdAsync(string userId)
    {
        // Try to read the user from the repository
        var user = await geoGuessrUserRepository.ReadUserByUserIdAsync(userId).ConfigureAwait(false);
        
        // If the user was found
        if (user != null)
        {
            return user;
        }
        
        // Read the user from GeoGuessr
        user = await geoGuessrAccess.ReadUserAsync(userId).ConfigureAwait(false);
        
        // If the user was not found
        if (user == null)
        {
            return null;
        }
        
        // Save the user
        var createdUser = await createOrUpdateUserUseCase.CreateOrUpdateUserAsync(user).ConfigureAwait(false);
        
        return createdUser;
    }
}