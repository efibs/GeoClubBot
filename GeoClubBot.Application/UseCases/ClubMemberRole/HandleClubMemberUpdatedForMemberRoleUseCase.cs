using Configuration;
using MediatR;
using Microsoft.Extensions.Options;
using UseCases.OutputPorts.Discord;
using UseCases.UseCases.ClubMembers;

namespace UseCases.UseCases.ClubMemberRole;

public class HandleClubMemberUpdatedForMemberRoleUseCase(IDiscordServerRolesAccess rolesAccess,
    IOptions<GeoGuessrConfiguration> geoGuessrConfig) : INotificationHandler<ClubMemberUpdatedEvent>
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

        // Get the role ID for this club
        var roleId = geoGuessrConfig.Value.GetClub(notification.NewClubMember.ClubId).RoleId;

        // If the club has no role configured, nothing to do
        if (roleId == null)
        {
            return;
        }

        // Get the users discord user id
        var discordUserId = notification.NewClubMember.User.DiscordUserId.Value;

        // Get if he is now a member and should have the member role
        var isMember = notification.NewClubMember.IsCurrentlyMember;

        // If the user is a member
        if (isMember)
        {
            // Give him the member role
            await rolesAccess.AddRoleToMembersByUserIdsAsync([discordUserId], roleId.Value).ConfigureAwait(false);
        }
        else
        {
            // Take the role away
            await rolesAccess.RemoveRolesFromUserAsync(discordUserId, [roleId.Value]).ConfigureAwait(false);
        }
    }
}
