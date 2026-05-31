using Entities.Events;
using MediatR;
using Microsoft.Extensions.Logging;
using UseCases.OutputPorts.Repositories;

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
                LogFailedToDeletePrivateChannel(logger, notification.Nickname, result.Error.Message);
            }
        }
        catch (Exception e)
        {
            LogUnhandled(logger, e);
        }
    }

    [LoggerMessage(LogLevel.Information,
        "Handling account unlinked for deleting private text channel for club member '{clubMemberNickname}'...")]
    static partial void LogDeletingPrivateChannel(ILogger<HandleAccountUnlinkedForPrivateChannelUseCase> logger,
        string clubMemberNickname);

    [LoggerMessage(LogLevel.Warning, "Failed to delete member private channel for member '{clubMemberNickname}': {Error}")]
    static partial void LogFailedToDeletePrivateChannel(ILogger<HandleAccountUnlinkedForPrivateChannelUseCase> logger,
        string clubMemberNickname, string error);

    [LoggerMessage(LogLevel.Error, "Error while handling HandleAccountUnlinkedForPrivateChannelUseCase")]
    static partial void LogUnhandled(ILogger<HandleAccountUnlinkedForPrivateChannelUseCase> logger, Exception ex);
}
