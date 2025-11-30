using Constants;
using Entities;
using Microsoft.Extensions.Configuration;
using UseCases.InputPorts.GeoGuessrAccountLinking;
using UseCases.InputPorts.Users;
using UseCases.OutputPorts;
using UseCases.OutputPorts.Discord;

namespace UseCases.UseCases.GeoGuessrAccountLinking;

public class CompleteAccountLinkingUseCase(
    IUnitOfWork unitOfWork,
    ICreateOrUpdateUserUseCase createOrUpdateUserUseCase, 
    IReadOrSyncGeoGuessrUserUseCase readOrSyncGeoGuessrUserUseCase,
    IDiscordServerRolesAccess rolesAccess,
    IConfiguration config) : ICompleteAccountLinkingUseCase
{
    public async Task<(bool Successful, GeoGuessrUser? User)> CompleteLinkingAsync(ulong discordUserId, string geoGuessrUserId, string oneTimePassword)
    {
        // Read the request
        var request = await unitOfWork.AccountLinkingRequests.ReadRequestAsync(discordUserId, geoGuessrUserId).ConfigureAwait(false);
        
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
        var user = await readOrSyncGeoGuessrUserUseCase.ReadOrSyncGeoGuessrUserByUserIdAsync(geoGuessrUserId).ConfigureAwait(false);
        
        // If the user does not exist
        if (user == null)
        {
            throw new InvalidOperationException($"User with id {geoGuessrUserId} does not exist.");
        }
        
        // Update the user
        user.DiscordUserId = discordUserId;
        
        // Save the user
        await createOrUpdateUserUseCase.CreateOrUpdateUserAsync(user).ConfigureAwait(false);

        // Delete the linking request
        unitOfWork.AccountLinkingRequests.DeleteRequest(request);
        
        // Save changes
        await unitOfWork.SaveChangesAsync().ConfigureAwait(false);
        
        // Give the user the has linked role
        await rolesAccess.AddRoleToMembersByUserIdsAsync([discordUserId], _hasLinkedRoleId).ConfigureAwait(false);
        
        return (true, user);
    }
    
    private readonly ulong _hasLinkedRoleId = config.GetValue<ulong>(ConfigKeys.GeoGuessrAccountLinkingHasLinkedRoleIdConfigurationKey);
}