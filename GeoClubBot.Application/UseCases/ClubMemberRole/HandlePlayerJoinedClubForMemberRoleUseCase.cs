using Configuration;
using Entities.Events;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.OutputPorts.Discord;

namespace UseCases.UseCases.ClubMemberRole;

public class HandlePlayerJoinedClubForMemberRoleUseCase(
    IDiscordServerRolesAccess rolesAccess,
    IOptions<GeoGuessrConfiguration> geoGuessrConfig,
    ILogger<HandlePlayerJoinedClubForMemberRoleUseCase> logger) : INotificationHandler<PlayerJoinedClubEvent>
{
    public async Task Handle(PlayerJoinedClubEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            if (notification.DiscordUserId is null)
            {
                return;
            }

            var roleId = geoGuessrConfig.Value.GetClub(notification.ClubId).RoleId;
            if (roleId is null)
            {
                return;
            }

            await rolesAccess
                .AddRoleToMembersByUserIdsAsync([notification.DiscordUserId.Value], roleId.Value)
                .ConfigureAwait(false);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error while handling PlayerJoinedClubForMemberRoleUseCase");
        }
    }
}
