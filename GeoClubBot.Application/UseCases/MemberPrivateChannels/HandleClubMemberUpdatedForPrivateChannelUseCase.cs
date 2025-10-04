using Entities;
using MediatR;
using UseCases.InputPorts.ClubMembers;
using UseCases.InputPorts.MemberPrivateChannels;
using UseCases.OutputPorts;
using UseCases.UseCases.ClubMembers;

namespace UseCases.UseCases.MemberPrivateChannels;

public class HandleClubMemberUpdatedForPrivateChannelUseCase(
    ICreateMemberPrivateChannelUseCase createMemberPrivateChannelUseCase,
    IDeleteMemberPrivateChannelUseCase deleteMemberPrivateChannelUseCase) 
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
            // Create a text channel for him
            await createMemberPrivateChannelUseCase.CreatePrivateChannelAsync(notification.NewClubMember)
                .ConfigureAwait(false);
        }
        else
        {
            // Delete his private channel
            await deleteMemberPrivateChannelUseCase.DeletePrivateChannelAsync(notification.NewClubMember)
                .ConfigureAwait(false);
        }
    }
}