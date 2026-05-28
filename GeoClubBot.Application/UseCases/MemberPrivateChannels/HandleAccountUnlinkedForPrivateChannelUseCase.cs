using Entities.Events;
using MediatR;
using Microsoft.Extensions.Logging;
using UseCases.OutputPorts;

namespace UseCases.UseCases.MemberPrivateChannels;

public partial class HandleAccountUnlinkedForPrivateChannelUseCase(
    ISender mediator,
    IClubMemberRepository clubMembers,
    ILogger<HandleAccountUnlinkedForPrivateChannelUseCase> logger)
    : INotificationHandler<AccountUnlinkedEvent>
{
    public async Task Handle(AccountUnlinkedEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            LogDeletingPrivateChannel(logger, notification.Nickname);

            var clubMember = await clubMembers
                .ReadClubMemberByUserIdAsync(notification.UserId, cancellationToken)
                .ConfigureAwait(false);

            var result = await mediator
                .Send(new DeleteMemberPrivateChannelCommand(clubMember), cancellationToken)
                .ConfigureAwait(false);

            if (result.IsFailure)
            {
                logger.LogWarning("Failed to delete member private channel for member '{clubMemberNickname}': {Error}",
                    notification.Nickname, result.Error.Message);
            }
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
