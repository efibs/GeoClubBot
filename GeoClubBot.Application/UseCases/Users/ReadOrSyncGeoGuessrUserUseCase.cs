using Entities;
using UseCases.InputPorts.Users;
using UseCases.OutputPorts;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.OutputPorts.GeoGuessr.Assemblers;

namespace UseCases.UseCases.Users;

public class ReadOrSyncGeoGuessrUserUseCase(IUnitOfWork unitOfWork, 
    ICreateOrUpdateUserUseCase createOrUpdateUserUseCase,
    IGeoGuessrClient geoGuessrClient) : IReadOrSyncGeoGuessrUserUseCase
{
    public async Task<GeoGuessrUser?> ReadOrSyncGeoGuessrUserByUserIdAsync(string userId)
    {
        // Try to read the user from the repository
        var user = await unitOfWork.GeoGuessrUsers.ReadUserByUserIdAsync(userId).ConfigureAwait(false);
        
        // If the user was found
        if (user != null)
        {
            return user;
        }

        try
        {
            // Read the user from GeoGuessr
            var geoGuessrUserDto = await geoGuessrClient.ReadUserAsync(userId).ConfigureAwait(false);

            // Assemble the entity
            var entity = UserAssembler.AssembleEntity(geoGuessrUserDto);
            
            // Save the user
            var createdUser = await createOrUpdateUserUseCase.CreateOrUpdateUserAsync(entity).ConfigureAwait(false);
        
            return createdUser;
        }
        catch
        {
            return null;
        }
    }
}