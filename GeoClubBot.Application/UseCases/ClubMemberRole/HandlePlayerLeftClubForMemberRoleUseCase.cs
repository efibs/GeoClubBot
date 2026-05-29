using Configuration;
using Entities.Events;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.OutputPorts.Discord;

namespace UseCases.UseCases.ClubMemberRole;

public partial class HandlePlayerLeftClubForMemberRoleUseCase(
    IDiscordServerRolesAccess rolesAccess,
    IOptions<GeoGuessrConfiguration> geoGuessrConfig,
    ILogger<HandlePlayerLeftClubForMemberRoleUseCase> logger) : INotificationHandler<PlayerLeftClubEvent>
{
    public async Task Handle(PlayerLeftClubEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            if (notification.DiscordUserId is null)
            {
                return;
            }

            var roleId = geoGuessrConfig.Value.GetClub(notification.OldClubId).RoleId;
            if (roleId is null)
            {
                return;
            }

            await rolesAccess
                .RemoveRolesFromUserAsync(notification.DiscordUserId.Value, [roleId.Value])
                .ConfigureAwait(false);
        }
        catch (Exception e)
        {
            LogUnhandled(logger, e);
        }
    }

    [LoggerMessage(LogLevel.Error, "Error while handling HandlePlayerLeftClubForMemberRoleUseCase")]
    static partial void LogUnhandled(ILogger<HandlePlayerLeftClubForMemberRoleUseCase> logger, Exception ex);
}
