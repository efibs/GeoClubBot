using Entities.Events;
using MediatR;
using Microsoft.Extensions.Logging;
using UseCases.OutputPorts;

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

            if (notification.PrivateTextChannelId is not null)
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
                .Send(new CreateMemberPrivateChannelCommand(clubMember), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception e)
        {
            LogUnhandled(logger, e);
        }
    }

    [LoggerMessage(LogLevel.Information, "Detected join of member '{clubMemberNickname}'. Creating private channel...")]
    static partial void LogJoinDetected(ILogger<HandlePlayerJoinedClubForPrivateChannelUseCase> logger,
        string clubMemberNickname);

    [LoggerMessage(LogLevel.Error, "Error while handling HandlePlayerJoinedClubForPrivateChannelUseCase")]
    static partial void LogUnhandled(ILogger<HandlePlayerJoinedClubForPrivateChannelUseCase> logger, Exception ex);
}
