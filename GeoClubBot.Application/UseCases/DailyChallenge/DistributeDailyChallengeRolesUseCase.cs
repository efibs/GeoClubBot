using Constants;
using Entities;
using Microsoft.Extensions.Configuration;
using UseCases.InputPorts.DailyChallenge;
using UseCases.OutputPorts;

namespace UseCases.UseCases.DailyChallenge;

public class DistributeDailyChallengeRolesUseCase(IServerRolesAccess serverRolesAccess, IConfiguration config)
    : IDistributeDailyChallengeRolesUseCase
{
    public async Task DistributeDailyChallengeRolesAsync(List<ClubChallengeResult> results)
    {
        // Remove the roles from every player
        await serverRolesAccess.RemoveRoleFromAllPlayersAsync(_firstRoleId);
        await serverRolesAccess.RemoveRoleFromAllPlayersAsync(_secondRoleId);
        await serverRolesAccess.RemoveRoleFromAllPlayersAsync(_thirdRoleId);

        // Save the nicknames of the winners
        var firstPlayers = new HashSet<string>();
        var secondPlayers = new HashSet<string>();
        var thirdPlayers = new HashSet<string>();

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
                    .Where(p => !firstPlayers.Contains(p.Nickname) &&
                                !secondPlayers.Contains(p.Nickname) &&
                                !thirdPlayers.Contains(p.Nickname))
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
                            firstPlayers.Add(player.Nickname);
                            break;
                        case 2:
                            secondPlayers.Add(player.Nickname);
                            break;
                        case 3:
                            thirdPlayers.Add(player.Nickname);
                            break;
                    }
                }
            }
        }
        
        // Distribute the roles
        await serverRolesAccess.AddRoleToMembersByNicknameAsync(firstPlayers, _firstRoleId);
        await serverRolesAccess.AddRoleToMembersByNicknameAsync(secondPlayers, _secondRoleId);
        await serverRolesAccess.AddRoleToMembersByNicknameAsync(thirdPlayers, _thirdRoleId);
    }
    
    private readonly ulong _firstRoleId = config.GetValue<ulong>(ConfigKeys.DailyChallengesFirstRoleIdConfigurationKey);
    private readonly ulong _secondRoleId = config.GetValue<ulong>(ConfigKeys.DailyChallengesSecondRoleIdConfigurationKey);
    private readonly ulong _thirdRoleId = config.GetValue<ulong>(ConfigKeys.DailyChallengesThirdRoleIdConfigurationKey);
}