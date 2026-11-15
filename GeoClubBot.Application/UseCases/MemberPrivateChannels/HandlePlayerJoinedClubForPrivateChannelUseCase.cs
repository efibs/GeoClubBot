using Entities.Events;
using MediatR;
using Microsoft.Extensions.Logging;
using UseCases.OutputPorts.Repositories;

namespace UseCases.UseCases.MemberPrivateChannels;

public partial class HandlePlayerJoinedClubForPrivateChannelUseCase(
    ISender mediator,
    IClubMemberRepository clubMembers,
    ILogger<HandlePlayerJoinedClubForPrivateChannelUseCase> logger) : INotificationHandler<PlayerJoinedClubEvent>
{
    public async Task Handle(PlayerJoinedClubEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            LogJoinDetected(logger, notification.Nickname);

            if (notification.DiscordUserId is null)
            {
                return;
            }

            var clubMember = await clubMembers
                .ReadClubMemberByUserIdAsync(notification.UserId, cancellationToken)
                .ConfigureAwait(false);

            if (clubMember is null)
            {
                return;
            }

            if (clubMember.PrivateTextChannelId is null)
            {
                await mediator
                    .Send(new CreateMemberPrivateChannelCommand(clubMember), cancellationToken)
                    .ConfigureAwait(false);

                return;
            }

            if (clubMember.PrivateTextChannelArchivedAt is null)
            {
                // A live channel here means the leave before this join never archived it, so it may still
                // sit in the old club's category (#483). The move does nothing if it is already right.
                var moved = await mediator
                    .Send(new MoveMemberPrivateChannelToClubCommand(clubMember), cancellationToken)
                    .ConfigureAwait(false);

                if (moved.IsFailure)
                {
                    LogFailedToMovePrivateChannel(logger, notification.Nickname, moved.Error.Message);
                }

                return;
            }

            // They left and came back (most often a hop to the second club, which the sync sees as a
            // leave followed by a join): bring the same channel back instead of making a new one.
            var result = await mediator
                .Send(new RestoreMemberPrivateChannelCommand(clubMember), cancellationToken)
                .ConfigureAwait(false);

            if (result.IsFailure)
            {
                LogFailedToRestorePrivateChannel(logger, notification.Nickname, result.Error.Message);
            }
        }
        catch (Exception e)
        {
            LogUnhandled(logger, e);
        }
    }

    [LoggerMessage(LogLevel.Warning, "Failed to restore member private channel for member '{clubMemberNickname}': {Error}")]
    static partial void LogFailedToRestorePrivateChannel(ILogger<HandlePlayerJoinedClubForPrivateChannelUseCase> logger,
        string clubMemberNickname, string error);

    [LoggerMessage(LogLevel.Warning, "Failed to move member private channel for member '{clubMemberNickname}': {Error}")]
    static partial void LogFailedToMovePrivateChannel(ILogger<HandlePlayerJoinedClubForPrivateChannelUseCase> logger,
        string clubMemberNickname, string error);

    [LoggerMessage(LogLevel.Information,
        "Detected join of member '{clubMemberNickname}'. Creating or restoring private channel...")]
    static partial void LogJoinDetected(ILogger<HandlePlayerJoinedClubForPrivateChannelUseCase> logger,
        string clubMemberNickname);

    [LoggerMessage(LogLevel.Error, "Error while handling HandlePlayerJoinedClubForPrivateChannelUseCase")]
    static partial void LogUnhandled(ILogger<HandlePlayerJoinedClubForPrivateChannelUseCase> logger, Exception ex);
}
