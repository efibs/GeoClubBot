using Constants;
using MediatR;
using Microsoft.Extensions.Configuration;
using UseCases.OutputPorts;
using UseCases.OutputPorts.Discord;
using UseCases.UseCases.Users;

namespace UseCases.UseCases.ClubMemberRole;

public class HandleUserUpdatedForMemberRoleUseCase(
    IUnitOfWork unitOfWork,
    IDiscordServerRolesAccess rolesAccess, 
    IConfiguration config) : INotificationHandler<UserUpdatedEvent>
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
            // Get the old discord user id
            var discordUserId = notification.OldUser.DiscordUserId.Value;
            
            // Take the role away
            await rolesAccess.RemoveRolesFromUserAsync(discordUserId, [_clubMemberRoleId]).ConfigureAwait(false);
        }
        
        // If the user now has no discord user id set
        if (notification.NewUser.DiscordUserId.HasValue == false)
        {
            return;
        }
            
        // Try to read the club member.
        // Do not sync him here to avoid duplicate role adding
        // due to a created event being thrown and also being handled.
        // If the user is not in the database yet, he will be soon. Then
        // the created event get's thrown and he gets the role.
        var clubMember = await unitOfWork.ClubMembers
            .ReadClubMemberByUserIdAsync(notification.NewUser.UserId)
            .ConfigureAwait(false);
            
        // If the user is not a member
        if (clubMember?.IsCurrentlyMember != true)
        {
            return;
        }
        
        // Get the new discord user id
        var newDiscordUserId = notification.NewUser.DiscordUserId.Value;
        
        // Give him the member role
        await rolesAccess.AddRoleToMembersByUserIdsAsync([newDiscordUserId], _clubMemberRoleId).ConfigureAwait(false);
    }
    
    
    
    private readonly ulong _clubMemberRoleId = config.GetValue<ulong>(ConfigKeys.GeoGuessrAccountLinkingClubMemberRoleIdConfigurationKey);
}