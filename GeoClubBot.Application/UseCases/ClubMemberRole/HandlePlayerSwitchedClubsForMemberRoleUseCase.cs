using Configuration;
using Entities;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.OutputPorts.Discord;
using UseCases.UseCases.ClubMembers;

namespace UseCases.UseCases.ClubMemberRole;

public class HandlePlayerSwitchedClubsForMemberRoleUseCase(
    IDiscordServerRolesAccess rolesAccess,
    IOptions<GeoGuessrConfiguration> geoGuessrConfig,
    ILogger<HandlePlayerSwitchedClubsForMemberRoleUseCase> logger) : INotificationHandler<PlayerSwitchedClubsEvent>
{
    public async Task Handle(PlayerSwitchedClubsEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            // Try to get the discord user id
            var discordUserId = _getDiscordUserId(notification);

            // Check if the event is relevant
            if (discordUserId is null)
            {
                // The event is not relevant if the user does not
                // have his accounts linked
                return;
            }

            // Remove the old role
            await _removeOldRoleAsync(notification.OldClubMember, discordUserId.Value).ConfigureAwait(false);

            // Give the new role
            await _giveNewRoleAsync(notification.NewClubMember, discordUserId.Value).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error while handling HandlePlayerSwitchedClubsForMemberRoleUseCase");
        }
    }

    private ulong? _getDiscordUserId(PlayerSwitchedClubsEvent notification)
    {
        return notification.NewClubMember.User.DiscordUserId ?? notification.OldClubMember.User.DiscordUserId;
    }

    private async Task _removeOldRoleAsync(ClubMember oldClubMember, ulong discordUserId)
    {
        // Get the role ID for the old club
        var oldRoleId = geoGuessrConfig.Value.GetClub(oldClubMember.ClubId!.Value).RoleId;

        // If the club has no role configured, nothing to do
        if (oldRoleId == null)
        {
            return;
        }

        // Take the role away
        await rolesAccess.RemoveRolesFromUserAsync(discordUserId, [oldRoleId.Value]).ConfigureAwait(false);
    }

    private async Task _giveNewRoleAsync(ClubMember newClubMember, ulong discordUserId)
    {
        // Get the role ID for the new club
        var newRoleId = geoGuessrConfig.Value.GetClub(newClubMember.ClubId!.Value).RoleId;

        // If the club has no role configured, nothing to do
        if (newRoleId == null)
        {
            return;
        }

        // Give him the member role
        await rolesAccess.AddRoleToMembersByUserIdsAsync([discordUserId], newRoleId.Value).ConfigureAwait(false);
    }
}