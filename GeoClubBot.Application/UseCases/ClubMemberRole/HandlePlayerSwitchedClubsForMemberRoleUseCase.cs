using Configuration;
using Entities.Events;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.OutputPorts.Discord;

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
            if (notification.DiscordUserId is null)
            {
                return;
            }

            var discordUserId = notification.DiscordUserId.Value;

            var oldRoleId = geoGuessrConfig.Value.GetClub(notification.OldClubId).RoleId;
            if (oldRoleId is not null)
            {
                await rolesAccess
                    .RemoveRolesFromUserAsync(discordUserId, [oldRoleId.Value])
                    .ConfigureAwait(false);
            }

            var newRoleId = geoGuessrConfig.Value.GetClub(notification.NewClubId).RoleId;
            if (newRoleId is not null)
            {
                await rolesAccess
                    .AddRoleToMembersByUserIdsAsync([discordUserId], newRoleId.Value)
                    .ConfigureAwait(false);
            }
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error while handling HandlePlayerSwitchedClubsForMemberRoleUseCase");
        }
    }
}
