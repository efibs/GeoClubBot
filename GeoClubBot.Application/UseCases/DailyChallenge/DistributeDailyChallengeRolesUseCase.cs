using Constants;
using Entities;
using Microsoft.Extensions.Configuration;
using UseCases.InputPorts.DailyChallenge;
using UseCases.InputPorts.Organization;
using UseCases.OutputPorts;

namespace UseCases.UseCases.DailyChallenge;

public class DistributeDailyChallengeRolesUseCase(IReadOrSyncGeoGuessrUserUseCase readOrSyncGeoGuessrUserUseCase,
    IServerRolesAccess serverRolesAccess, 
    IConfiguration config)
    : IDistributeDailyChallengeRolesUseCase
{
    public async Task DistributeDailyChallengeRolesAsync(List<ClubChallengeResult> results)
    {
        // Remove the roles from every player
        await serverRolesAccess.RemoveRoleFromAllPlayersAsync(_firstRoleId);
        await serverRolesAccess.RemoveRoleFromAllPlayersAsync(_secondRoleId);
        await serverRolesAccess.RemoveRoleFromAllPlayersAsync(_thirdRoleId);

        // Save the nicknames of the winners
        var firstPlayersGeoGuessrUserIds = new HashSet<string>();
        var secondPlayersGeoGuessrUserIds = new HashSet<string>();
        var thirdPlayersGeoGuessrUserIds = new HashSet<string>();

        // Group the results by role priority
        var rolePriorityGroupedResults = results
            .GroupBy(r => r.RolePriority);

        // Order the grouped results by priority
        var rolePriorityOrderedGroupedResults = rolePriorityGroupedResults
            .OrderByDescending(x => x.Key);
        
        // For every priority
        foreach (var rolePriorityGroup in rolePriorityOrderedGroupedResults)
        {
            // Remove the players that are already winners
            var cleanedResults = rolePriorityGroup
                .Select(r => r.Players
                    .Where(p => !firstPlayersGeoGuessrUserIds.Contains(p.Nickname) &&
                                !secondPlayersGeoGuessrUserIds.Contains(p.Nickname) &&
                                !thirdPlayersGeoGuessrUserIds.Contains(p.Nickname))
                    .ToList())
                .ToList();
            
            // For every cleaned result
            foreach (var cleanedResult in cleanedResults)
            {
                // Add the first three players to the winners
                var place = 1;
                foreach (var player in cleanedResult)
                {
                    switch (place++)
                    {
                        case 1:
                            firstPlayersGeoGuessrUserIds.Add(player.UserId);
                            break;
                        case 2:
                            secondPlayersGeoGuessrUserIds.Add(player.UserId);
                            break;
                        case 3:
                            thirdPlayersGeoGuessrUserIds.Add(player.UserId);
                            break;
                    }
                }
            }
        }
        
        // Convert to Discord user ids
        var firstPlayers = await _geoGuessrUserIdsToDiscordUserIdsAsync(firstPlayersGeoGuessrUserIds);
        var secondPlayers = await _geoGuessrUserIdsToDiscordUserIdsAsync(secondPlayersGeoGuessrUserIds);
        var thirdPlayers = await _geoGuessrUserIdsToDiscordUserIdsAsync(thirdPlayersGeoGuessrUserIds);
        
        // Distribute the roles
        await serverRolesAccess.AddRoleToMembersByUserIdsAsync(firstPlayers, _firstRoleId);
        await serverRolesAccess.AddRoleToMembersByUserIdsAsync(secondPlayers, _secondRoleId);
        await serverRolesAccess.AddRoleToMembersByUserIdsAsync(thirdPlayers, _thirdRoleId);
    }

    private async Task<List<ulong>> _geoGuessrUserIdsToDiscordUserIdsAsync(IEnumerable<string> geoGuessrUserIds)
    {
        // Create a new list
        var discordUserIds = new List<ulong>();

        // For every GeoGuessr user id
        foreach (var geoGuessrUserId in geoGuessrUserIds)
        {
            // Try to read the user
            var geoGuessrUser =
                await readOrSyncGeoGuessrUserUseCase.ReadOrSyncGeoGuessrUserByUserIdAsync(geoGuessrUserId);
            
            // If there is a discord user id set
            if (geoGuessrUser?.DiscordUserId != null)
            {
                // Add the discord user id to the list
                discordUserIds.Add(geoGuessrUser.DiscordUserId.Value);
            }
        }
        
        return discordUserIds;
    }
    
    private readonly ulong _firstRoleId = config.GetValue<ulong>(ConfigKeys.DailyChallengesFirstRoleIdConfigurationKey);
    private readonly ulong _secondRoleId = config.GetValue<ulong>(ConfigKeys.DailyChallengesSecondRoleIdConfigurationKey);
    private readonly ulong _thirdRoleId = config.GetValue<ulong>(ConfigKeys.DailyChallengesThirdRoleIdConfigurationKey);
}