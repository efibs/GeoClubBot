using MediatR;
using Microsoft.Extensions.Logging;
using UseCases.InputPorts.MemberPrivateChannels;
using UseCases.UseCases.ClubMembers;

namespace UseCases.UseCases.MemberPrivateChannels;

public partial class HandleClubMemberCreatedForPrivateChannelUseCase(
    ICreateMemberPrivateChannelUseCase createMemberPrivateChannelUseCase,
    IDeleteMemberPrivateChannelUseCase deleteMemberPrivateChannelUseCase,
    ILogger<HandleClubMemberCreatedForPrivateChannelUseCase> logger) 
    : INotificationHandler<ClubMemberCreatedEvent>
{
    public async Task Handle(ClubMemberCreatedEvent notification, CancellationToken cancellationToken)
    {
        // Check if the event is relevant
        if (notification.ClubMember.User.DiscordUserId.HasValue == false)
        {
            // The event is not relevant if the user does not 
            // have his accounts linked
            return;
        }
        
        // Get if he is now a member and should have the member role
        var isMember = notification.ClubMember.IsCurrentlyMember;
        
        // If the user is a member
        if (isMember)
        {
            // Log
            LogCreatingPrivateChannel(logger, notification.ClubMember.User.Nickname);
            
            // Create a private channel for him
            await createMemberPrivateChannelUseCase.CreatePrivateChannelAsync(notification.ClubMember).ConfigureAwait(false);
        }
        else
        {
            // Log
            LogDeletingPrivateChannel(logger, notification.ClubMember.User.Nickname);
            
            // Delete his private channel just to be sure.
            // Normally he shouldn't have the channel at this point.
            await deleteMemberPrivateChannelUseCase.DeletePrivateChannelAsync(notification.ClubMember).ConfigureAwait(false);
        }
    }
    
    [LoggerMessage(LogLevel.Information, "Handling club member created for creating private text channel for club member '{clubMemberNickname}'...")]
    static partial void LogCreatingPrivateChannel(ILogger<HandleClubMemberCreatedForPrivateChannelUseCase> logger, string clubMemberNickname);
    
    [LoggerMessage(LogLevel.Information, "Handling club member created for deleting private text channel for club member '{clubMemberNickname}'...")]
    static partial void LogDeletingPrivateChannel(ILogger<HandleClubMemberCreatedForPrivateChannelUseCase> logger, string clubMemberNickname);
}