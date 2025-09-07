using Constants;
using Entities;
using Microsoft.Extensions.Configuration;
using UseCases.InputPorts.Club;
using UseCases.InputPorts.GeoGuessrAccountLinking;
using UseCases.InputPorts.Organization;
using UseCases.OutputPorts;

namespace UseCases.UseCases.GeoGuessrAccountLinking;

public class CompleteAccountLinkingUseCase(IAccountLinkingRequestRepository accountLinkingRequestRepository, 
    IGeoGuessrUserRepository geoGuessrUserRepository, 
    IReadOrSyncGeoGuessrUserUseCase readOrSyncGeoGuessrUserUseCase,
    ISyncClubMemberRoleUseCase syncClubMemberRoleUseCase,
    IServerRolesAccess rolesAccess,
    IConfiguration config) : ICompleteAccountLinkingUseCase
{
    public async Task<(bool Successful, GeoGuessrUser? User)> CompleteLinkingAsync(ulong discordUserId, string geoGuessrUserId, string oneTimePassword)
    {
        // Read the request
        var request = await accountLinkingRequestRepository.ReadRequestAsync(discordUserId, geoGuessrUserId);
        
        // If the request does not exist
        if (request == null)
        {
            throw new InvalidOperationException($"There is no linking request for Discord user with id {discordUserId} and GeoGuessr user with id {geoGuessrUserId}");
        }
        
        // If the password does not match
        if (request.OneTimePassword != oneTimePassword)
        {
            return (false, null);
        }
        
        // Read the user
        var user = await readOrSyncGeoGuessrUserUseCase.ReadOrSyncGeoGuessrUserByUserIdAsync(geoGuessrUserId);
        
        // If the user does not exist
        if (user == null)
        {
            throw new InvalidOperationException($"User with id {geoGuessrUserId} does not exist.");
        }
        
        // Update the user
        user.DiscordUserId = discordUserId;
        
        // Save the user
        await geoGuessrUserRepository.CreateOrUpdateUserAsync(user);

        // Delete the linking request
        await accountLinkingRequestRepository.DeleteRequestAsync(discordUserId, geoGuessrUserId);
        
        // Give the user the has linked role
        await rolesAccess.AddRoleToMembersByUserIdsAsync([discordUserId], _hasLinkedRoleId);
        
        // Sync the users member role
        await syncClubMemberRoleUseCase.SyncUserClubMemberRoleAsync(discordUserId, geoGuessrUserId);
        
        return (true, user);
    }
    
    private readonly ulong _hasLinkedRoleId = config.GetValue<ulong>(ConfigKeys.GeoGuessrAccountLinkingHasLinkedRoleIdConfigurationKey);
}