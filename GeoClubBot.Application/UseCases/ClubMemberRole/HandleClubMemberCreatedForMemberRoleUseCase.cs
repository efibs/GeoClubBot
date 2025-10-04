using Constants;
using MediatR;
using Microsoft.Extensions.Configuration;
using UseCases.OutputPorts;
using UseCases.UseCases.ClubMembers;

namespace UseCases.UseCases.ClubMemberRole;

public class HandleClubMemberCreatedForMemberRoleUseCase(IServerRolesAccess rolesAccess, 
    IConfiguration config) : INotificationHandler<ClubMemberCreatedEvent>
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
        
        // Get the members discord user id
        var discordUserId = notification.ClubMember.User.DiscordUserId.Value;
        
        // If the user is a member
        if (isMember)
        {
            // Give him the member role
            await rolesAccess.AddRoleToMembersByUserIdsAsync([discordUserId], _clubMemberRoleId).ConfigureAwait(false);
        }
        else
        {
            // Take the role away just to be sure. 
            // Normally he shouldn't have the role at this point
            await rolesAccess.RemoveRolesFromUserAsync(discordUserId, [_clubMemberRoleId]).ConfigureAwait(false);
        }
    }
    
    private readonly ulong _clubMemberRoleId = config.GetValue<ulong>(ConfigKeys.GeoGuessrAccountLinkingClubMemberRoleIdConfigurationKey);
}