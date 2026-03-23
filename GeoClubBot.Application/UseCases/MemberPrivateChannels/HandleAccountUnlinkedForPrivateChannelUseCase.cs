using MediatR;
using Microsoft.Extensions.Logging;
using UseCases.InputPorts.MemberPrivateChannels;
using UseCases.OutputPorts;
using UseCases.UseCases.GeoGuessrAccountLinking;

namespace UseCases.UseCases.MemberPrivateChannels;

public partial class HandleAccountUnlinkedForPrivateChannelUseCase(IDeleteMemberPrivateChannelUseCase deleteMemberPrivateChannelUseCase, 
    IUnitOfWork unitOfWork,
    ILogger<HandleAccountUnlinkedForPrivateChannelUseCase> logger)
    : INotificationHandler<AccountUnlinkedEvent>
{
    public async Task Handle(AccountUnlinkedEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            // Get the user 
            var user = notification.User;

            // Log
            LogDeletingPrivateChannel(logger, user.Nickname);

            // Try to read the club member.
            var clubMember = await unitOfWork.ClubMembers
                .ReadClubMemberByUserIdAsync(user.UserId)
                .ConfigureAwait(false);

            // Create the private channel
            var successful = await deleteMemberPrivateChannelUseCase.DeletePrivateChannelAsync(clubMember).ConfigureAwait(false);
            
            // If the delete was not successful
            if (!successful)
            {
                logger.LogWarning("Failed to delete member private channel for member '{clubMemberNickname}'", user.Nickname);
            }
            
            // Save the changes
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