using Constants;
using Entities;
using MediatR;
using Microsoft.Extensions.Configuration;
using UseCases.InputPorts.GeoGuessrAccountLinking;
using UseCases.OutputPorts;
using UseCases.OutputPorts.Discord;
using UseCases.UseCases.Users;

namespace UseCases.UseCases.GeoGuessrAccountLinking;

public class CompleteAccountLinkingUseCase(
    IUnitOfWork unitOfWork,
    ISender mediator,
    IDiscordServerRolesAccess rolesAccess,
    IConfiguration config) : ICompleteAccountLinkingUseCase
{
    public async Task<(bool Successful, GeoGuessrUser? User)> CompleteLinkingAsync(ulong discordUserId, string geoGuessrUserId, string oneTimePassword)
    {
        var request = await unitOfWork.AccountLinkingRequests
            .ReadRequestAsync(discordUserId, geoGuessrUserId)
            .ConfigureAwait(false);

        if (request is null)
        {
            throw new InvalidOperationException(
                $"There is no linking request for Discord user with id {discordUserId} and GeoGuessr user with id {geoGuessrUserId}");
        }

        if (request.OneTimePassword != oneTimePassword)
        {
            return (false, null);
        }

        // Ensure the user exists in the database (sync from API if necessary).
        var user = await mediator
            .Send(new ReadOrSyncGeoGuessrUserByUserIdQuery(geoGuessrUserId))
            .ConfigureAwait(false);

        if (user is null)
        {
            throw new InvalidOperationException($"User with id {geoGuessrUserId} does not exist.");
        }

        // Re-read for update so the link mutation runs on a tracked instance.
        var trackedUser = await unitOfWork.GeoGuessrUsers
            .ReadForUpdateByUserIdAsync(geoGuessrUserId)
            .ConfigureAwait(false);

        if (trackedUser is null)
        {
            return (false, null);
        }

        trackedUser.LinkDiscord(discordUserId);

        unitOfWork.AccountLinkingRequests.DeleteRequest(request);

        await unitOfWork.SaveChangesAsync().ConfigureAwait(false);

        await rolesAccess
            .AddRoleToMembersByUserIdsAsync([discordUserId], _hasLinkedRoleId)
            .ConfigureAwait(false);

        return (true, trackedUser);
    }

    private readonly ulong _hasLinkedRoleId =
        config.GetValue<ulong>(ConfigKeys.GeoGuessrAccountLinkingHasLinkedRoleIdConfigurationKey);
}
