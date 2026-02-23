using MediatR;
using Microsoft.Extensions.Logging;
using UseCases.InputPorts.MemberPrivateChannels;
using UseCases.UseCases.ClubMembers;

namespace UseCases.UseCases.MemberPrivateChannels;

public partial class HandleClubMemberUpdatedForPrivateChannelUseCase(
    ICreateMemberPrivateChannelUseCase createMemberPrivateChannelUseCase,
    IDeleteMemberPrivateChannelUseCase deleteMemberPrivateChannelUseCase,
    ILogger<HandleClubMemberUpdatedForPrivateChannelUseCase> logger) 
    : INotificationHandler<ClubMemberUpdatedEvent>
{
    public async Task Handle(ClubMemberUpdatedEvent notification, CancellationToken cancellationToken)
    {
        // Check if the event is relevant
        if (notification.OldClubMember.IsCurrentlyMember == notification.NewClubMember.IsCurrentlyMember)
        {
            // The event is not relevant if the membership status did not change
            return;
        }
        
        // Check if the user has his GeoGuessr account linked
        if (notification.NewClubMember.User.DiscordUserId == null)
        {
            // If the user has not linked his account, nothing to do
            return;
        }

        // Get if he is now a member and should have the member role
        var isMember = notification.NewClubMember.IsCurrentlyMember;
        
        // If the user is a member
        if (isMember)
        {
            // Log
            LogCreatingPrivateChannel(logger, notification.NewClubMember.User.Nickname);
            
            // Create a text channel for him
            await createMemberPrivateChannelUseCase.CreatePrivateChannelAsync(notification.NewClubMember)
                .ConfigureAwait(false);
        }
        else
        {
            // Log
            LogDeletingPrivateChannel(logger, notification.NewClubMember.User.Nickname);
            
            // Delete his private channel
            await deleteMemberPrivateChannelUseCase.DeletePrivateChannelAsync(notification.NewClubMember)
                .ConfigureAwait(false);
        }
    }
    
    [LoggerMessage(LogLevel.Information, "Handling club member updated for creating private text channel for club member '{clubMemberNickname}'...")]
    static partial void LogCreatingPrivateChannel(ILogger<HandleClubMemberUpdatedForPrivateChannelUseCase> logger, string clubMemberNickname);
    
    [LoggerMessage(LogLevel.Information, "Handling club member updated for deleting private text channel for club member '{clubMemberNickname}'...")]
    static partial void LogDeletingPrivateChannel(ILogger<HandleClubMemberUpdatedForPrivateChannelUseCase> logger, string clubMemberNickname);
}