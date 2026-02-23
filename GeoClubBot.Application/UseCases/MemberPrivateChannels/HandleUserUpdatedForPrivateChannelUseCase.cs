using Entities;
using MediatR;
using Microsoft.Extensions.Logging;
using UseCases.InputPorts.MemberPrivateChannels;
using UseCases.OutputPorts;
using UseCases.UseCases.Users;

namespace UseCases.UseCases.MemberPrivateChannels;

public partial class HandleUserUpdatedForPrivateChannelUseCase(
    ICreateMemberPrivateChannelUseCase createMemberPrivateChannelUseCase,
    IDeleteMemberPrivateChannelUseCase deleteMemberPrivateChannelUseCase,
    IUnitOfWork unitOfWork,
    ILogger<HandleUserUpdatedForPrivateChannelUseCase> logger) 
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
        // Log
        LogDeletingPrivateChannel(logger, oldUser.Nickname);
        
        // Try to read the club member.
        var clubMember = await unitOfWork.ClubMembers
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
        var clubMember = await unitOfWork.ClubMembers
            .ReadClubMemberByUserIdAsync(newUser.UserId)
            .ConfigureAwait(false);
        
        // If the user is not a member
        if (clubMember?.IsCurrentlyMember != true)
        {
            return;
        }
        
        // Log
        LogCreatingPrivateChannel(logger, newUser.Nickname);
        
        // Create the text channel
        await createMemberPrivateChannelUseCase.CreatePrivateChannelAsync(clubMember).ConfigureAwait(false);
    }
    
    [LoggerMessage(LogLevel.Information, "Handling user updated for creating private text channel for club member '{clubMemberNickname}'...")]
    static partial void LogCreatingPrivateChannel(ILogger<HandleUserUpdatedForPrivateChannelUseCase> logger, string clubMemberNickname);
    
    [LoggerMessage(LogLevel.Information, "Handling user updated for deleting private text channel for club member '{clubMemberNickname}'...")]
    static partial void LogDeletingPrivateChannel(ILogger<HandleUserUpdatedForPrivateChannelUseCase> logger, string clubMemberNickname);
}