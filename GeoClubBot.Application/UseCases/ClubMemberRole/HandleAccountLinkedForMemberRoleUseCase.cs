using Configuration;
using Entities.Events;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.OutputPorts;
using UseCases.OutputPorts.Discord;

namespace UseCases.UseCases.ClubMemberRole;

public partial class HandleAccountLinkedForMemberRoleUseCase(
    IClubMemberRepository clubMembers,
    IDiscordServerRolesAccess rolesAccess,
    IOptions<GeoGuessrConfiguration> geoGuessrConfig,
    ILogger<HandleAccountLinkedForMemberRoleUseCase> logger)
    : INotificationHandler<AccountLinkedEvent>
{
    public async Task Handle(AccountLinkedEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            // Do not sync the member here to avoid duplicate role-add when the corresponding
            // PlayerJoinedClubEvent fires for a freshly-synced user.
            var clubMember = await clubMembers
                .ReadClubMemberByUserIdAsync(notification.UserId, cancellationToken)
                .ConfigureAwait(false);

            if (clubMember?.ClubId is null)
            {
                return;
            }

            var roleId = geoGuessrConfig.Value.GetClub(clubMember.ClubId.Value).RoleId;
            if (roleId is null)
            {
                return;
            }

            await rolesAccess
                .AddRoleToMembersByUserIdsAsync([notification.DiscordUserId], roleId.Value, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception e)
        {
            LogUnhandled(logger, e);
        }
    }

    [LoggerMessage(LogLevel.Error, "Error while handling HandleAccountLinkedForMemberRoleUseCase")]
    static partial void LogUnhandled(ILogger<HandleAccountLinkedForMemberRoleUseCase> logger, Exception ex);
}
