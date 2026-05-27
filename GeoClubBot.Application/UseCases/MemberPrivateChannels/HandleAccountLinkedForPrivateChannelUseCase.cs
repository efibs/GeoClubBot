using Entities.Events;
using MediatR;
using Microsoft.Extensions.Logging;
using UseCases.OutputPorts;

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
                return;
            }

            LogCreatingPrivateChannel(logger, notification.Nickname);

            await mediator
                .Send(new CreateMemberPrivateChannelCommand(clubMember), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error while handling HandleAccountLinkedForPrivateChannelUseCase");
        }
    }

    [LoggerMessage(LogLevel.Information,
        "Handling account linked for creating private text channel for club member '{clubMemberNickname}'...")]
    static partial void LogCreatingPrivateChannel(ILogger<HandleAccountLinkedForPrivateChannelUseCase> logger,
        string clubMemberNickname);
}
