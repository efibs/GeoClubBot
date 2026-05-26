using Entities.Events;
using MediatR;
using Microsoft.Extensions.Logging;
using UseCases.OutputPorts;

namespace UseCases.UseCases.MemberPrivateChannels;

public partial class HandleAccountUnlinkedForPrivateChannelUseCase(
    ISender mediator,
    IUnitOfWork unitOfWork,
    ILogger<HandleAccountUnlinkedForPrivateChannelUseCase> logger)
    : INotificationHandler<AccountUnlinkedEvent>
{
    public async Task Handle(AccountUnlinkedEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            LogDeletingPrivateChannel(logger, notification.Nickname);

            var clubMember = await unitOfWork.ClubMembers
                .ReadClubMemberByUserIdAsync(notification.UserId)
                .ConfigureAwait(false);

            var successful = await mediator
                .Send(new DeleteMemberPrivateChannelCommand(clubMember), cancellationToken)
                .ConfigureAwait(false);

            if (!successful)
            {
                logger.LogWarning("Failed to delete member private channel for member '{clubMemberNickname}'", notification.Nickname);
            }

            await unitOfWork.SaveChangesAsync().ConfigureAwait(false);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error while handling HandleAccountUnlinkedForPrivateChannelUseCase");
        }
    }

    [LoggerMessage(LogLevel.Information,
        "Handling account unlinked for deleting private text channel for club member '{clubMemberNickname}'...")]
    static partial void LogDeletingPrivateChannel(ILogger<HandleAccountUnlinkedForPrivateChannelUseCase> logger,
        string clubMemberNickname);
}
