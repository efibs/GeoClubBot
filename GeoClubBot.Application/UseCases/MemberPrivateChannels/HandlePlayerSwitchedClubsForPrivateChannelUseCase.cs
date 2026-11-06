using Entities.Events;
using MediatR;
using Microsoft.Extensions.Logging;
using UseCases.OutputPorts.Repositories;
using Utilities;

namespace UseCases.UseCases.MemberPrivateChannels;

/// <summary>
/// A member went straight from one club to the other: their channel follows them into the new club's
/// category (whose header only that club's role sees). A member who somehow has no channel gets one,
/// and an archived one is restored.
/// </summary>
public partial class HandlePlayerSwitchedClubsForPrivateChannelUseCase(
    ISender mediator,
    IClubMemberRepository clubMembers,
    ILogger<HandlePlayerSwitchedClubsForPrivateChannelUseCase> logger)
    : INotificationHandler<PlayerSwitchedClubsEvent>
{
    public async Task Handle(PlayerSwitchedClubsEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            if (notification.DiscordUserId is null)
            {
                return;
            }

            LogSwitchDetected(logger, notification.Nickname);

            var clubMember = await clubMembers
                .ReadClubMemberByUserIdAsync(notification.UserId, cancellationToken)
                .ConfigureAwait(false);

            if (clubMember?.ClubId is null)
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

            var error = clubMember.PrivateTextChannelArchivedAt is null
                ? (await mediator
                    .Send(new MoveMemberPrivateChannelToClubCommand(clubMember), cancellationToken)
                    .ConfigureAwait(false)).Error
                : (await mediator
                    .Send(new RestoreMemberPrivateChannelCommand(clubMember), cancellationToken)
                    .ConfigureAwait(false)).Error;

            if (error != Error.None)
            {
                LogFailedToMovePrivateChannel(logger, notification.Nickname, error.Message);
            }
        }
        catch (Exception e)
        {
            LogUnhandled(logger, e);
        }
    }

    [LoggerMessage(LogLevel.Information,
        "Detected club switch of member '{clubMemberNickname}'. Moving private channel to the new club...")]
    static partial void LogSwitchDetected(ILogger<HandlePlayerSwitchedClubsForPrivateChannelUseCase> logger,
        string clubMemberNickname);

    [LoggerMessage(LogLevel.Warning, "Failed to move member private channel for member '{clubMemberNickname}': {Error}")]
    static partial void LogFailedToMovePrivateChannel(ILogger<HandlePlayerSwitchedClubsForPrivateChannelUseCase> logger,
        string clubMemberNickname, string error);

    [LoggerMessage(LogLevel.Error, "Error while handling HandlePlayerSwitchedClubsForPrivateChannelUseCase")]
    static partial void LogUnhandled(ILogger<HandlePlayerSwitchedClubsForPrivateChannelUseCase> logger, Exception ex);
}
