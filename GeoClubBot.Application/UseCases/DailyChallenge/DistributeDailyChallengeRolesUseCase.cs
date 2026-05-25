using Constants;
using Entities;
using MediatR;
using Microsoft.Extensions.Configuration;
using UseCases.InputPorts.DailyChallenge;
using UseCases.OutputPorts.Discord;
using UseCases.UseCases.Users;

namespace UseCases.UseCases.DailyChallenge;

public class DistributeDailyChallengeRolesUseCase(
    ISender mediator,
    IDiscordServerRolesAccess discordServerRolesAccess,
    IConfiguration config)
    : IDistributeDailyChallengeRolesUseCase
{
    public async Task DistributeDailyChallengeRolesAsync(List<ClubChallengeResult> results)
    {
        // Remove the roles from every player
        await discordServerRolesAccess.RemoveRoleFromAllPlayersAsync(_firstRoleId).ConfigureAwait(false);
        await discordServerRolesAccess.RemoveRoleFromAllPlayersAsync(_secondRoleId).ConfigureAwait(false);
        await discordServerRolesAccess.RemoveRoleFromAllPlayersAsync(_thirdRoleId).ConfigureAwait(false);

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
        var firstPlayers = await mediator.Send(new GeoGuessrUserIdsToDiscordUserIdsQuery(firstPlayersGeoGuessrUserIds)).ConfigureAwait(false);
        var secondPlayers = await mediator.Send(new GeoGuessrUserIdsToDiscordUserIdsQuery(secondPlayersGeoGuessrUserIds)).ConfigureAwait(false);
        var thirdPlayers = await mediator.Send(new GeoGuessrUserIdsToDiscordUserIdsQuery(thirdPlayersGeoGuessrUserIds)).ConfigureAwait(false);
        
        // Distribute the roles
        await discordServerRolesAccess.AddRoleToMembersByUserIdsAsync(firstPlayers, _firstRoleId).ConfigureAwait(false);
        await discordServerRolesAccess.AddRoleToMembersByUserIdsAsync(secondPlayers, _secondRoleId).ConfigureAwait(false);
        await discordServerRolesAccess.AddRoleToMembersByUserIdsAsync(thirdPlayers, _thirdRoleId).ConfigureAwait(false);
    }
    
    private readonly ulong _firstRoleId = config.GetValue<ulong>(ConfigKeys.DailyChallengesFirstRoleIdConfigurationKey);
    private readonly ulong _secondRoleId = config.GetValue<ulong>(ConfigKeys.DailyChallengesSecondRoleIdConfigurationKey);
    private readonly ulong _thirdRoleId = config.GetValue<ulong>(ConfigKeys.DailyChallengesThirdRoleIdConfigurationKey);
}