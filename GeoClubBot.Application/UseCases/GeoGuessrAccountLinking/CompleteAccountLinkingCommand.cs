using Configuration;
using Entities;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.Abstractions;
using UseCases.OutputPorts.Repositories;
using UseCases.OutputPorts.Discord;
using UseCases.UseCases.Users;
using Utilities;

namespace UseCases.UseCases.GeoGuessrAccountLinking;

public sealed record CompleteAccountLinkingCommand(ulong DiscordUserId, string GeoGuessrUserId, string OneTimePassword)
    : ICommand<Result<GeoGuessrUser>>;

public sealed partial class CompleteAccountLinkingHandler(
    IAccountLinkingRequestRepository requests,
    IGeoGuessrUserRepository users,
    ISender mediator,
    IDiscordServerRolesAccess rolesAccess,
    IOptions<GeoGuessrAccountLinkingConfiguration> accountLinkingOptions,
    ILogger<CompleteAccountLinkingHandler> logger) : IRequestHandler<CompleteAccountLinkingCommand, Result<GeoGuessrUser>>
{
    private readonly ulong _hasLinkedRoleId = accountLinkingOptions.Value.HasLinkedRoleId;

    public async Task<Result<GeoGuessrUser>> Handle(CompleteAccountLinkingCommand request, CancellationToken cancellationToken)
    {
        var linkingRequest = await requests
            .ReadRequestAsync(request.DiscordUserId, request.GeoGuessrUserId, cancellationToken)
            .ConfigureAwait(false);

        if (linkingRequest is null)
        {
            return Error.NotFound(
                "account_linking.request_not_found",
                $"There is no linking request for Discord user with id {request.DiscordUserId} and GeoGuessr user with id {request.GeoGuessrUserId}.");
        }

        if (!linkingRequest.Matches(request.OneTimePassword))
        {
            return Error.Validation(
                "account_linking.otp_mismatch",
                "Account linking failed: Wrong password. Please try again.");
        }

        // Ensure the user exists locally (sync from API on first contact)
        var ensured = await mediator
            .Send(new ReadOrSyncGeoGuessrUserByUserIdQuery(request.GeoGuessrUserId), cancellationToken)
            .ConfigureAwait(false);
        if (ensured.IsFailure)
        {
            return Error.Unexpected(
                "account_linking.user_sync_failed",
                $"User with id {request.GeoGuessrUserId} could not be synced from the GeoGuessr API.");
        }

        var trackedUser = await users
            .ReadForUpdateByUserIdAsync(request.GeoGuessrUserId, cancellationToken)
            .ConfigureAwait(false);
        if (trackedUser is null)
        {
            return Error.NotFound(
                "account_linking.user_not_found",
                $"GeoGuessr user with id {request.GeoGuessrUserId} was not found after syncing.");
        }

        trackedUser.LinkDiscord(request.DiscordUserId);
        requests.DeleteRequest(linkingRequest);

        // Save so the linked Discord ID is visible to the role-assignment call below.
        // The UnitOfWorkBehavior would otherwise only save on return.
        // We call the role assignment outside the unit of work because it's an external side effect
        // — the persisted change must be in place first.
        await rolesAccess
            .AddRoleToMembersByUserIdsAsync([request.DiscordUserId], _hasLinkedRoleId, cancellationToken)
            .ConfigureAwait(false);

        LogAccountLinked(logger, request.DiscordUserId, request.GeoGuessrUserId);
        return trackedUser;
    }

    [LoggerMessage(LogLevel.Information, "Discord user {DiscordUserId} linked to GeoGuessr user {GeoGuessrUserId}.")]
    static partial void LogAccountLinked(ILogger<CompleteAccountLinkingHandler> logger, ulong discordUserId, string geoGuessrUserId);
}
