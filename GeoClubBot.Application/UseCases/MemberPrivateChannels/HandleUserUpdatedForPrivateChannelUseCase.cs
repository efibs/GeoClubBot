using Entities;
using MediatR;
using UseCases.InputPorts.MemberPrivateChannels;
using UseCases.OutputPorts;
using UseCases.UseCases.Users;

namespace UseCases.UseCases.MemberPrivateChannels;

public class HandleUserUpdatedForPrivateChannelUseCase(
    ICreateMemberPrivateChannelUseCase createMemberPrivateChannelUseCase,
    IDeleteMemberPrivateChannelUseCase deleteMemberPrivateChannelUseCase,
    IClubMemberRepository clubMemberRepository) 
    : INotificationHandler<UserUpdatedEvent>
{
    public async Task Handle(UserUpdatedEvent notification, CancellationToken cancellationToken)
    {
        // Check if the event is relevant
        if (notification.OldUser.DiscordUserId == notification.NewUser.DiscordUserId)
        {
            // The event is not relevant if the discord user id did not change
            return;
        }
        
        // If the user had a discord user id set
        if (notification.OldUser.DiscordUserId.HasValue)
        {
            // Delete the text channel for the old user
            await _handleOldUserAsync(notification.OldUser).ConfigureAwait(false);
        }
        
        // If the user now has no discord user id set
        if (notification.NewUser.DiscordUserId.HasValue == false)
        {
            return;
        }
        
        // Create the text channel for the new user
        await _handleNewUserAsync(notification.NewUser).ConfigureAwait(false);
    }

    private async Task _handleOldUserAsync(GeoGuessrUser oldUser)
    {
        // Try to read the club member.
        var clubMember = await clubMemberRepository
            .ReadClubMemberByUserIdAsync(oldUser.UserId)
            .ConfigureAwait(false);
        
        // Create the private channel
        await deleteMemberPrivateChannelUseCase.DeletePrivateChannelAsync(clubMember).ConfigureAwait(false);
    }

    private async Task _handleNewUserAsync(GeoGuessrUser newUser)
    {
        // Try to read the club member.
        // Do not sync him here to avoid duplicate channel creating
        // due to a created event being thrown and also being handled.
        // If the user is not in the database yet, he will be soon. Then
        // the created event get's thrown and he gets the channel.
        var clubMember = await clubMemberRepository
            .ReadClubMemberByUserIdAsync(newUser.UserId)
            .ConfigureAwait(false);
        
        // If the user is not a member
        if (clubMember?.IsCurrentlyMember != true)
        {
            return;
        }
        
        // Create the text channel
        await createMemberPrivateChannelUseCase.CreatePrivateChannelAsync(clubMember).ConfigureAwait(false);
    }
}