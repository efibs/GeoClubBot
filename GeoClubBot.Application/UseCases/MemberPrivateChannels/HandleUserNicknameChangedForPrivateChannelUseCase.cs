using Entities;
using MediatR;
using UseCases.OutputPorts;
using UseCases.UseCases.Users;

namespace UseCases.UseCases.MemberPrivateChannels;

public class HandleUserNicknameChangedForPrivateChannelUseCase(
    IClubMemberRepository clubMemberRepository,
    ITextChannelAccess textChannelAccess) 
    : INotificationHandler<UserUpdatedEvent>
{
    public async Task Handle(UserUpdatedEvent notification, CancellationToken cancellationToken)
    {
        // Check if the event is relevant
        if (notification.OldUser.DiscordUserId != notification.NewUser.DiscordUserId)
        {
            // The event is not relevant if the discord user id did change
            return;
        }
        
        // Check if the event is relevant
        if (notification.OldUser.Nickname == notification.NewUser.Nickname)
        {
            // The event is not relevant if the nickname did not change
            return;
        }

        // Try to read the club member.
        var clubMember = await clubMemberRepository
            .ReadClubMemberByUserIdAsync(notification.NewUser.UserId)
            .ConfigureAwait(false);
        
        // If the user is not a member
        if (clubMember?.IsCurrentlyMember != true)
        {
            return;
        }
        
        // If the user has no text channel yet
        if (clubMember.PrivateTextChannelId.HasValue == false)
        {
            return;
        }
        
        // Get the text channel name
        var textChannelName = $"{clubMember.User.Nickname.ToLowerInvariant()}-private-channel";
        
        // Build the new text channel
        var newTextChannel = new TextChannel(clubMember.PrivateTextChannelId.Value)
        {
            Name = textChannelName
        };
        
        // Update the text channel
        await textChannelAccess.UpdateTextChannelAsync(newTextChannel).ConfigureAwait(false);
    }
}