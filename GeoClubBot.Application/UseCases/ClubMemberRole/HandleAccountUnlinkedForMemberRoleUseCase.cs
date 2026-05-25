using Configuration;
using Entities.Events;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.OutputPorts.Discord;

namespace UseCases.UseCases.ClubMemberRole;

public class HandleAccountUnlinkedForMemberRoleUseCase(
    IDiscordServerRolesAccess rolesAccess,
    IOptions<GeoGuessrConfiguration> geoGuessrConfig,
    ILogger<HandleAccountUnlinkedForMemberRoleUseCase> logger)
    : INotificationHandler<AccountUnlinkedEvent>
{
    public async Task Handle(AccountUnlinkedEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            var allRoleIds = geoGuessrConfig.Value.Clubs
                .Where(c => c.RoleId.HasValue)
                .Select(c => c.RoleId!.Value)
                .ToArray();

            if (allRoleIds.Length > 0)
            {
                await rolesAccess
                    .RemoveRolesFromUserAsync(notification.DiscordUserId, allRoleIds)
                    .ConfigureAwait(false);
            }
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error while handling HandleAccountUnlinkedForMemberRoleUseCase");
        }
    }
}
