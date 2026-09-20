using Entities.Events;
using MediatR;
using Microsoft.Extensions.Logging;
using UseCases.OutputPorts.Repositories;

namespace UseCases.UseCases.MemberPrivateChannels;

public partial class HandlePlayerLeftClubForPrivateChannelUseCase(
    ISender mediator,
    IClubMemberRepository clubMembers,
    ILogger<HandlePlayerLeftClubForPrivateChannelUseCase> logger) : INotificationHandler<PlayerLeftClubEvent>
{
    public async Task Handle(PlayerLeftClubEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            LogLeaveDetected(logger, notification.Nickname);

            if (notification.DiscordUserId is null)
            {
                return;
            }

            var clubMember = await clubMembers
                .ReadClubMemberByUserIdAsync(notification.UserId, cancellationToken)
                .ConfigureAwait(false);

            if (clubMember?.PrivateTextChannelId is null)
            {
                return;
            }

            var result = await mediator
                .Send(new ArchiveMemberPrivateChannelCommand(clubMember), cancellationToken)
                .ConfigureAwait(false);

            if (result.IsFailure)
            {
                LogFailedToArchivePrivateChannel(logger, notification.Nickname, result.Error.Message);
            }
        }
        catch (Exception e)
        {
            LogUnhandled(logger, e);
        }
    }

    [LoggerMessage(LogLevel.Information,
        "Detected leave of member '{clubMemberNickname}'. Archiving private channel...")]
    static partial void LogLeaveDetected(ILogger<HandlePlayerLeftClubForPrivateChannelUseCase> logger,
        string clubMemberNickname);

    [LoggerMessage(LogLevel.Warning, "Failed to archive member private channel for member '{clubMemberNickname}': {Error}")]
    static partial void LogFailedToArchivePrivateChannel(ILogger<HandlePlayerLeftClubForPrivateChannelUseCase> logger,
        string clubMemberNickname, string error);

    [LoggerMessage(LogLevel.Error, "Error while handling HandlePlayerLeftClubForPrivateChannelUseCase")]
    static partial void LogUnhandled(ILogger<HandlePlayerLeftClubForPrivateChannelUseCase> logger, Exception ex);
}
