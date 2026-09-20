using Entities.Events;
using MediatR;
using Microsoft.Extensions.Logging;
using UseCases.OutputPorts.Repositories;

namespace UseCases.UseCases.MemberPrivateChannels;

public partial class HandleAccountLinkedForPrivateChannelUseCase(
    ISender mediator,
    IClubMemberRepository clubMembers,
    ILogger<HandleAccountLinkedForPrivateChannelUseCase> logger)
    : INotificationHandler<AccountLinkedEvent>
{
    public async Task Handle(AccountLinkedEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            // Avoid duplicate channel creation: if the user was just synced, the PlayerJoinedClubEvent
            // will pick this up. Only handle existing members here.
            var clubMember = await clubMembers
                .ReadClubMemberByUserIdAsync(notification.UserId, cancellationToken)
                .ConfigureAwait(false);

            if (clubMember?.ClubId is null)
            {
                return;
            }

            if (clubMember.PrivateTextChannelId is not null)
            {
                if (clubMember.PrivateTextChannelArchivedAt is null)
                {
                    return;
                }

                // An archived channel is theirs again the moment they are back in a club.
                var restoreResult = await mediator
                    .Send(new RestoreMemberPrivateChannelCommand(clubMember), cancellationToken)
                    .ConfigureAwait(false);

                if (restoreResult.IsFailure)
                {
                    LogFailedToRestorePrivateChannel(logger, notification.Nickname, restoreResult.Error.Message);
                }

                return;
            }

            LogCreatingPrivateChannel(logger, notification.Nickname);

            await mediator
                .Send(new CreateMemberPrivateChannelCommand(clubMember), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception e)
        {
            LogUnhandled(logger, e);
        }
    }

    [LoggerMessage(LogLevel.Information,
        "Handling account linked for creating private text channel for club member '{clubMemberNickname}'...")]
    static partial void LogCreatingPrivateChannel(ILogger<HandleAccountLinkedForPrivateChannelUseCase> logger,
        string clubMemberNickname);

    [LoggerMessage(LogLevel.Warning, "Failed to restore member private channel for member '{clubMemberNickname}': {Error}")]
    static partial void LogFailedToRestorePrivateChannel(ILogger<HandleAccountLinkedForPrivateChannelUseCase> logger,
        string clubMemberNickname, string error);

    [LoggerMessage(LogLevel.Error, "Error while handling HandleAccountLinkedForPrivateChannelUseCase")]
    static partial void LogUnhandled(ILogger<HandleAccountLinkedForPrivateChannelUseCase> logger, Exception ex);
}
