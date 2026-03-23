using Configuration;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.OutputPorts;
using UseCases.OutputPorts.Discord;
using UseCases.UseCases.GeoGuessrAccountLinking;

namespace UseCases.UseCases.ClubMemberRole;

public class HandleAccountLinkedForMemberRoleUseCase(
    IUnitOfWork unitOfWork,
    IDiscordServerRolesAccess rolesAccess,
    IOptions<GeoGuessrConfiguration> geoGuessrConfig,
    ILogger<HandleAccountLinkedForMemberRoleUseCase> logger) 
    : INotificationHandler<AccountLinkedEvent>
{
    public async Task Handle(AccountLinkedEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            // Try to read the club member.
            // Do not sync him here to avoid duplicate role adding
            // due to a created event being thrown and also being handled.
            // If the user is not in the database yet, he will be soon. Then
            // the created event get's thrown and he gets the role.
            var clubMember = await unitOfWork.ClubMembers
                .ReadClubMemberByUserIdAsync(notification.User.UserId)
                .ConfigureAwait(false);

            // If the user is not a member
            if (clubMember?.ClubId is null)
            {
                return;
            }

            // Get the role ID for the user's club
            var roleId = geoGuessrConfig.Value.GetClub(clubMember.ClubId.Value).RoleId;

            // If the club has no role configured, nothing to do
            if (roleId == null)
            {
                return;
            }

            // Get the new discord user id
            var newDiscordUserId = notification.User.DiscordUserId!.Value;

            // Give him the member role
            await rolesAccess.AddRoleToMembersByUserIdsAsync([newDiscordUserId], roleId.Value).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error while handling HandleAccountLinkedForMemberRoleUseCase");
        }
    }
}