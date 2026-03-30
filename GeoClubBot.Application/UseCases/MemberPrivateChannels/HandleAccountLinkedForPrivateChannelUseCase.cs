using MediatR;
using Microsoft.Extensions.Logging;
using UseCases.InputPorts.MemberPrivateChannels;
using UseCases.OutputPorts;
using UseCases.UseCases.GeoGuessrAccountLinking;

namespace UseCases.UseCases.MemberPrivateChannels;

public partial class HandleAccountLinkedForPrivateChannelUseCase(
    ICreateMemberPrivateChannelUseCase createMemberPrivateChannelUseCase, 
    IUnitOfWork unitOfWork,
    ILogger<HandleAccountLinkedForPrivateChannelUseCase> logger)
    : INotificationHandler<AccountLinkedEvent>
{
    public async Task Handle(AccountLinkedEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            // Get the user
            var user = notification.User;
            
            // Try to read the club member.
            // Do not sync him here to avoid duplicate channel creating
            // due to a created event being thrown and also being handled.
            // If the user is not in the database yet, he will be soon. Then
            // the created event get's thrown and he gets the channel.
            var clubMember = await unitOfWork.ClubMembers
                .ReadClubMemberByUserIdAsync(user.UserId)
                .ConfigureAwait(false);

            // If the user is not a member
            if (clubMember?.ClubId is null)
            {
                return;
            }

            // Check if the member already has a private channel
            if (clubMember.PrivateTextChannelId is not null)
            {
                return;
            }

            // Log
            LogCreatingPrivateChannel(logger, user.Nickname);

            // Create the text channel
            await createMemberPrivateChannelUseCase.CreatePrivateChannelAsync(clubMember).ConfigureAwait(false);
            
            // Save the changes
            await unitOfWork.SaveChangesAsync().ConfigureAwait(false);
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