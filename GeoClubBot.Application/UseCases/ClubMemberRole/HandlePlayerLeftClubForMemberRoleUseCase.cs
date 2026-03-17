using Configuration;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.OutputPorts.Discord;
using UseCases.UseCases.ClubMembers;

namespace UseCases.UseCases.ClubMemberRole;

public class HandlePlayerLeftClubForMemberRoleUseCase(
    IDiscordServerRolesAccess rolesAccess,
    IOptions<GeoGuessrConfiguration> geoGuessrConfig,
    ILogger<HandlePlayerLeftClubForMemberRoleUseCase> logger) : INotificationHandler<PlayerLeftClubEvent>
{
    public async Task Handle(PlayerLeftClubEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            // Check if the event is relevant
            if (notification.ClubMember.User.DiscordUserId.HasValue == false)
            {
                // The event is not relevant if the user does not
                // have his accounts linked
                return;
            }

            // Get the role ID for this club
            var roleId = geoGuessrConfig.Value.GetClub(notification.ClubMember.ClubId!.Value).RoleId;

            // If the club has no role configured, nothing to do
            if (roleId == null)
            {
                return;
            }

            // Get the members discord user id
            var discordUserId = notification.ClubMember.User.DiscordUserId.Value;

            // Take the member role away
            await rolesAccess.RemoveRolesFromUserAsync(discordUserId, [roleId.Value]).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error while handling HandlePlayerLeftClubForMemberRoleUseCase");
        }
    }
}