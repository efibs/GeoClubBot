using Configuration;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.OutputPorts.Discord;
using UseCases.UseCases.GeoGuessrAccountLinking;

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
            // Get the old discord user id
            var discordUserId = notification.User.DiscordUserId!.Value;

            // Remove all configured club role IDs since we don't know which club they were in
            var allRoleIds = geoGuessrConfig.Value.Clubs
                .Where(c => c.RoleId.HasValue)
                .Select(c => c.RoleId!.Value)
                .ToArray();

            if (allRoleIds.Length > 0)
            {
                await rolesAccess.RemoveRolesFromUserAsync(discordUserId, allRoleIds).ConfigureAwait(false);
            }
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error while handling HandleAccountUnlinkedForMemberRoleUseCase");
        }
    }
}