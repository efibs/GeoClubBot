using UseCases.InputPorts.GeoGuessrAccountLinking;
using UseCases.OutputPorts;

namespace UseCases.UseCases.GeoGuessrAccountLinking;

public class UnlinkAccountsUseCase(IGeoGuessrUserRepository geoGuessrUserRepository) : IUnlinkAccountsUseCase
{
    public async Task<bool> UnlinkAccountsAsync(ulong discordUserId, string geoGuessrUserId)
    {
        // Read the user
        var user = await geoGuessrUserRepository.ReadUserByUserIdAsync(geoGuessrUserId);
        
        // If the user does not exist
        if (user == null)
        {
            return false;
        }
        
        // If the user is not linked to the given discord user
        if (user.DiscordUserId != discordUserId)
        {
            return false;
        }
        
        // Remove the discord user id of the user
        user.DiscordUserId = null;
        
        // Update the user
        await geoGuessrUserRepository.CreateOrUpdateUserAsync(user);
        
        return true;
    }
}