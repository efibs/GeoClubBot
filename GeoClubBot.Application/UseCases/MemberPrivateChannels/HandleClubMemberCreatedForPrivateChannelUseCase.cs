using MediatR;
using UseCases.InputPorts.MemberPrivateChannels;
using UseCases.UseCases.ClubMembers;

namespace UseCases.UseCases.MemberPrivateChannels;

public class HandleClubMemberCreatedForPrivateChannelUseCase(
    ICreateMemberPrivateChannelUseCase createMemberPrivateChannelUseCase,
    IDeleteMemberPrivateChannelUseCase deleteMemberPrivateChannelUseCase) 
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
            // Create a private channel for him
            await createMemberPrivateChannelUseCase.CreatePrivateChannelAsync(notification.ClubMember).ConfigureAwait(false);
        }
        else
        {
            // Delete his private channel just to be sure.
            // Normally he shouldn't have the channel at this point.
            await deleteMemberPrivateChannelUseCase.DeletePrivateChannelAsync(notification.ClubMember).ConfigureAwait(false);
        }
    }
}