using Entities;
using MediatR;
using Microsoft.Extensions.Logging;
using UseCases.OutputPorts;
using UseCases.OutputPorts.Discord;
using UseCases.UseCases.Users;

namespace UseCases.UseCases.MemberPrivateChannels;

public partial class HandleUserNicknameChangedForPrivateChannelUseCase(
    IUnitOfWork unitOfWork,
    IDiscordTextChannelAccess discordTextChannelAccess,
    ILogger<HandleUserNicknameChangedForPrivateChannelUseCase> logger) 
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
        var clubMember = await unitOfWork.ClubMembers
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
        
        // Log
        LogRenamingPrivateChannel(logger, clubMember.User.Nickname);
        
        // Update the text channel
        await discordTextChannelAccess.UpdateTextChannelAsync(newTextChannel).ConfigureAwait(false);
    }
    
    [LoggerMessage(LogLevel.Information, "Handling user updated for renaming private text channel for club member '{clubMemberNickname}'...")]
    static partial void LogRenamingPrivateChannel(ILogger<HandleUserNicknameChangedForPrivateChannelUseCase> logger, string clubMemberNickname);
}