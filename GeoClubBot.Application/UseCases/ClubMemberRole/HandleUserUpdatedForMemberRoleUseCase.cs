using Configuration;
using MediatR;
using Microsoft.Extensions.Options;
using UseCases.OutputPorts;
using UseCases.OutputPorts.Discord;
using UseCases.UseCases.Users;

namespace UseCases.UseCases.ClubMemberRole;

public class HandleUserUpdatedForMemberRoleUseCase(
    IUnitOfWork unitOfWork,
    IDiscordServerRolesAccess rolesAccess,
    IOptions<GeoGuessrConfiguration> geoGuessrConfig) : INotificationHandler<UserUpdatedEvent>
{
    public async Task Handle(UserUpdatedEvent notification, CancellationToken cancellationToken)
    {
        // Check if the event is relevant
        if (notification.OldUser.DiscordUserId == notification.NewUser.DiscordUserId)
        {
            // The event is not relevant if the discord user id did not change
            return;
        }

        // If the user had a discord user id set
        if (notification.OldUser.DiscordUserId.HasValue)
        {
            // Get the old discord user id
            var discordUserId = notification.OldUser.DiscordUserId.Value;

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

        // If the user now has no discord user id set
        if (notification.NewUser.DiscordUserId.HasValue == false)
        {
            return;
        }

        // Try to read the club member.
        // Do not sync him here to avoid duplicate role adding
        // due to a created event being thrown and also being handled.
        // If the user is not in the database yet, he will be soon. Then
        // the created event get's thrown and he gets the role.
        var clubMember = await unitOfWork.ClubMembers
            .ReadClubMemberByUserIdAsync(notification.NewUser.UserId)
            .ConfigureAwait(false);

        // If the user is not a member
        if (clubMember?.IsCurrentlyMember != true)
        {
            return;
        }

        // Get the role ID for the user's club
        var roleId = geoGuessrConfig.Value.GetClub(clubMember.ClubId).RoleId;

        // If the club has no role configured, nothing to do
        if (roleId == null)
        {
            return;
        }

        // Get the new discord user id
        var newDiscordUserId = notification.NewUser.DiscordUserId.Value;

        // Give him the member role
        await rolesAccess.AddRoleToMembersByUserIdsAsync([newDiscordUserId], roleId.Value).ConfigureAwait(false);
    }
}
