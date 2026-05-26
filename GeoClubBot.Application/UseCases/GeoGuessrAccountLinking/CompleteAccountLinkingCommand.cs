using Constants;
using Entities;
using MediatR;
using Microsoft.Extensions.Configuration;
using UseCases.Abstractions;
using UseCases.OutputPorts;
using UseCases.OutputPorts.Discord;
using UseCases.UseCases.Users;

namespace UseCases.UseCases.GeoGuessrAccountLinking;

public sealed record CompleteAccountLinkingCommand(ulong DiscordUserId, string GeoGuessrUserId, string OneTimePassword)
    : ICommand<CompleteAccountLinkingResult>;

public sealed record CompleteAccountLinkingResult(bool Successful, GeoGuessrUser? User);

public sealed class CompleteAccountLinkingHandler(
    IAccountLinkingRequestRepository requests,
    IGeoGuessrUserRepository users,
    ISender mediator,
    IDiscordServerRolesAccess rolesAccess,
    IConfiguration config) : IRequestHandler<CompleteAccountLinkingCommand, CompleteAccountLinkingResult>
{
    private readonly ulong _hasLinkedRoleId =
        config.GetValue<ulong>(ConfigKeys.GeoGuessrAccountLinkingHasLinkedRoleIdConfigurationKey);

    public async Task<CompleteAccountLinkingResult> Handle(CompleteAccountLinkingCommand request, CancellationToken cancellationToken)
    {
        var linkingRequest = await requests
            .ReadRequestAsync(request.DiscordUserId, request.GeoGuessrUserId)
            .ConfigureAwait(false);

        if (linkingRequest is null)
        {
            throw new InvalidOperationException(
                $"There is no linking request for Discord user with id {request.DiscordUserId} and GeoGuessr user with id {request.GeoGuessrUserId}");
        }

        if (!linkingRequest.Matches(request.OneTimePassword))
        {
            return new CompleteAccountLinkingResult(false, null);
        }

        // Ensure the user exists locally (sync from API on first contact)
        var ensured = await mediator
            .Send(new ReadOrSyncGeoGuessrUserByUserIdQuery(request.GeoGuessrUserId), cancellationToken)
            .ConfigureAwait(false);
        if (ensured is null)
        {
            throw new InvalidOperationException($"User with id {request.GeoGuessrUserId} does not exist.");
        }

        var trackedUser = await users
            .ReadForUpdateByUserIdAsync(request.GeoGuessrUserId)
            .ConfigureAwait(false);
        if (trackedUser is null)
        {
            return new CompleteAccountLinkingResult(false, null);
        }

        trackedUser.LinkDiscord(request.DiscordUserId);
        requests.DeleteRequest(linkingRequest);

        // Save so the linked Discord ID is visible to the role-assignment call below.
        // The UnitOfWorkBehavior would otherwise only save on return.
        // We call the role assignment outside the unit of work because it's an external side effect
        // — the persisted change must be in place first.
        await rolesAccess
            .AddRoleToMembersByUserIdsAsync([request.DiscordUserId], _hasLinkedRoleId)
            .ConfigureAwait(false);

        return new CompleteAccountLinkingResult(true, trackedUser);
    }
}
