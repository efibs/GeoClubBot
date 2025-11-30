using Constants;
using Microsoft.Extensions.Configuration;
using UseCases.InputPorts.GeoGuessrAccountLinking;
using UseCases.InputPorts.Users;
using UseCases.OutputPorts;
using UseCases.OutputPorts.Discord;

namespace UseCases.UseCases.GeoGuessrAccountLinking;

public class UnlinkAccountsUseCase(IGeoGuessrUserRepository geoGuessrUserRepository, 
    ICreateOrUpdateUserUseCase createOrUpdateUserUseCase,
    IDiscordServerRolesAccess rolesAccess, 
    IConfiguration config) : IUnlinkAccountsUseCase
{
    public async Task<bool> UnlinkAccountsAsync(ulong discordUserId, string geoGuessrUserId)
    {
        // Read the user
        var user = await geoGuessrUserRepository.ReadUserByUserIdAsync(geoGuessrUserId).ConfigureAwait(false);
        
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
        await createOrUpdateUserUseCase.CreateOrUpdateUserAsync(user).ConfigureAwait(false);
        
        // Remove has linked role from user
        await rolesAccess.RemoveRolesFromUserAsync(discordUserId, [_hasLinkedRoleId, _clubMemberRoleId]).ConfigureAwait(false);
        
        return true;
    }
    
    private readonly ulong _hasLinkedRoleId = config.GetValue<ulong>(ConfigKeys.GeoGuessrAccountLinkingHasLinkedRoleIdConfigurationKey);
    private readonly ulong _clubMemberRoleId = config.GetValue<ulong>(ConfigKeys.GeoGuessrAccountLinkingClubMemberRoleIdConfigurationKey);
}