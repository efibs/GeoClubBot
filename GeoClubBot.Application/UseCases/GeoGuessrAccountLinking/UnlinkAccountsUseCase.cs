using Configuration;
using Constants;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using UseCases.InputPorts.GeoGuessrAccountLinking;
using UseCases.OutputPorts;
using UseCases.OutputPorts.Discord;

namespace UseCases.UseCases.GeoGuessrAccountLinking;

public class UnlinkAccountsUseCase(IUnitOfWork unitOfWork,
    IDiscordServerRolesAccess rolesAccess,
    IConfiguration config,
    IOptions<GeoGuessrConfiguration> geoGuessrConfig) : IUnlinkAccountsUseCase
{
    public async Task<bool> UnlinkAccountsAsync(ulong discordUserId, string geoGuessrUserId)
    {
        // Read the user
        var user = await unitOfWork.GeoGuessrUsers.ReadUserByUserIdAsync(geoGuessrUserId).ConfigureAwait(false);

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

        // Unlink the Discord account from the GeoGuessr user
        var unlinkedUser = await unitOfWork.GeoGuessrUsers.UnlinkDiscordAccountAsync(geoGuessrUserId).ConfigureAwait(false);

        // If the user was not successfully unlinked
        if (unlinkedUser is null)
        {
            return false;
        }
        
        // Create the unlinked event
        var unlinkedEvent = new AccountUnlinkedEvent(user);
        
        // Add to the entity
        unlinkedUser.AddDomainEvent(unlinkedEvent);
        
        // Save the changes
        await unitOfWork.SaveChangesAsync().ConfigureAwait(false);

        // Collect all roles to remove
        var rolesToRemove = new List<ulong> { _hasLinkedRoleId };

        // Add all configured club role IDs
        rolesToRemove.AddRange(
            geoGuessrConfig.Value.Clubs
                .Where(c => c.RoleId.HasValue)
                .Select(c => c.RoleId!.Value));

        // Remove has linked role and all club member roles from user
        await rolesAccess.RemoveRolesFromUserAsync(discordUserId, rolesToRemove.ToArray()).ConfigureAwait(false);

        return true;
    }

    private readonly ulong _hasLinkedRoleId = config.GetValue<ulong>(ConfigKeys.GeoGuessrAccountLinkingHasLinkedRoleIdConfigurationKey);
}
