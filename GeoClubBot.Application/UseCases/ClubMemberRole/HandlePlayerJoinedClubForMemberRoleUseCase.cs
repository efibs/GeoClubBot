using Configuration;
using Entities.Events;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.OutputPorts.Discord;

namespace UseCases.UseCases.ClubMemberRole;

public partial class HandlePlayerJoinedClubForMemberRoleUseCase(
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
            LogUnhandled(logger, e);
        }
    }

    [LoggerMessage(LogLevel.Error, "Error while handling PlayerJoinedClubForMemberRoleUseCase")]
    static partial void LogUnhandled(ILogger<HandlePlayerJoinedClubForMemberRoleUseCase> logger, Exception ex);
}
