using Entities;
using UseCases.InputPorts.Organization;
using UseCases.OutputPorts;
using UseCases.OutputPorts.GeoGuessr;

namespace UseCases.UseCases.Organization;

public class ReadOrSyncGeoGuessrUserUseCase(IGeoGuessrUserRepository geoGuessrUserRepository, IGeoGuessrAccess geoGuessrAccess) : IReadOrSyncGeoGuessrUserUseCase
{
    public async Task<GeoGuessrUser?> ReadOrSyncGeoGuessrUserByUserIdAsync(string userId)
    {
        // Try to read the user from the repository
        var user = await geoGuessrUserRepository.ReadUserByUserIdAsync(userId);
        
        // If the user was found
        if (user != null)
        {
            return user;
        }
        
        // Read the user from GeoGuessr
        var userDto = await geoGuessrAccess.ReadUserAsync(userId);
        
        // If the user was not found
        if (userDto == null)
        {
            return null;
        }
        
        // Create the user object
        var newUser = new GeoGuessrUser
        {
            UserId = userDto.Id,
            Nickname = userDto.Nick
        };
        
        // Save the user
        await geoGuessrUserRepository.CreateOrUpdateUserAsync(newUser);
        
        return newUser;
    }
}