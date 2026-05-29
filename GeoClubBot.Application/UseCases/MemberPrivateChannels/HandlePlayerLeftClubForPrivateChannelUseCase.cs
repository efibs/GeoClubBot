using Entities.Events;
using MediatR;
using Microsoft.Extensions.Logging;
using UseCases.OutputPorts;

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

            if (clubMember is null)
            {
                return;
            }

            await mediator
                .Send(new DeleteMemberPrivateChannelCommand(clubMember), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception e)
        {
            LogUnhandled(logger, e);
        }
    }

    [LoggerMessage(LogLevel.Information,
        "Detected leave of member '{clubMemberNickname}'. Removing private channel...")]
    static partial void LogLeaveDetected(ILogger<HandlePlayerLeftClubForPrivateChannelUseCase> logger,
        string clubMemberNickname);

    [LoggerMessage(LogLevel.Error, "Error while handling HandlePlayerLeftClubForPrivateChannelUseCase")]
    static partial void LogUnhandled(ILogger<HandlePlayerLeftClubForPrivateChannelUseCase> logger, Exception ex);
}
