using Configuration;
using MediatR;
using Microsoft.Extensions.Options;
using UseCases.OutputPorts.Discord;
using UseCases.UseCases.ClubMembers;

namespace UseCases.UseCases.ClubMemberRole;

public class HandleClubMemberCreatedForMemberRoleUseCase(IDiscordServerRolesAccess rolesAccess,
    IOptions<GeoGuessrConfiguration> geoGuessrConfig) : INotificationHandler<ClubMemberCreatedEvent>
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

        // Get the role ID for this club
        var roleId = geoGuessrConfig.Value.GetClub(notification.ClubMember.ClubId).RoleId;

        // If the club has no role configured, nothing to do
        if (roleId == null)
        {
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
            await rolesAccess.AddRoleToMembersByUserIdsAsync([discordUserId], roleId.Value).ConfigureAwait(false);
        }
        else
        {
            // Take the role away just to be sure.
            // Normally he shouldn't have the role at this point
            await rolesAccess.RemoveRolesFromUserAsync(discordUserId, [roleId.Value]).ConfigureAwait(false);
        }
    }
}
