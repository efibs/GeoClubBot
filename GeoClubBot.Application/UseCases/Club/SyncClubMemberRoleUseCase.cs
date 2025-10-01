using Constants;
using Microsoft.Extensions.Configuration;
using UseCases.InputPorts.Club;
using UseCases.OutputPorts;
using UseCases.OutputPorts.GeoGuessr;

namespace UseCases.UseCases.Club;

public class SyncClubMemberRoleUseCase(IGeoGuessrAccess geoGuessrAccess, 
    IGeoGuessrUserRepository geoGuessrUserRepository,
    IServerRolesAccess rolesAccess, 
    IConfiguration config) : ISyncClubMemberRoleUseCase
{
    public async Task SyncAllUsersClubMemberRoleAsync()
    {
        // Read the club members
        var clubMembers = await geoGuessrAccess.ReadClubMembersAsync(_clubId);
        
        // Get a hashset of all members GeoGuessr user ids
        var clubMemberGeoGuessrUserIds = clubMembers
            .Select(m => m.User.UserId)
            .ToHashSet();
        
        // Read all linked users
        var linkedUsers = await geoGuessrUserRepository.ReadAllLinkedUsersAsync();
        
        // For every linked user
        foreach (var linkedUser in linkedUsers)
        {
            // Check if he is a member
            var userIsMember = clubMemberGeoGuessrUserIds.Contains(linkedUser.UserId);
            
            // Sync the role
            await _syncRoleOfUser(userIsMember, linkedUser.DiscordUserId!.Value);
        }
    }

    public async Task SyncUserClubMemberRoleAsync(ulong discordUserId, string geoGuessrUserId)
    {
        // Read the club members
        var clubMembers = await geoGuessrAccess.ReadClubMembersAsync(_clubId);
        
        // Check if the user is a club member
        var userIsClubMember = clubMembers.Any(m => m.User.UserId == geoGuessrUserId);
        
        // Sync the role
        await _syncRoleOfUser(userIsClubMember, discordUserId);
    }

    private async Task _syncRoleOfUser(bool isClubMember, ulong discordUserId)
    {
        // If the user is a member
        if (isClubMember)
        {
            // Give him the member role
            await rolesAccess.AddRoleToMembersByUserIdsAsync([discordUserId], _clubMemberRoleId);
        }
        else
        {
            // Take the role away
            await rolesAccess.RemoveRolesFromUserAsync(discordUserId, [_clubMemberRoleId]);
        }
    }
    
    private readonly Guid _clubId = config.GetValue<Guid>(ConfigKeys.GeoGuessrClubIdConfigurationKey);
    private readonly ulong _clubMemberRoleId = config.GetValue<ulong>(ConfigKeys.GeoGuessrAccountLinkingClubMemberRoleIdConfigurationKey);
}