using Constants;
using Entities;
using MediatR;
using Microsoft.Extensions.Configuration;
using UseCases.Abstractions;
using UseCases.OutputPorts.Discord;
using UseCases.UseCases.Users;

namespace UseCases.UseCases.DailyChallenge;

public sealed record DistributeDailyChallengeRolesCommand(List<ClubChallengeResult> Results) : ICommand;

public sealed class DistributeDailyChallengeRolesHandler(
    ISender mediator,
    IDiscordServerRolesAccess discordServerRolesAccess,
    IConfiguration config) : IRequestHandler<DistributeDailyChallengeRolesCommand, Unit>
{
    private readonly ulong _firstRoleId = config.GetValue<ulong>(ConfigKeys.DailyChallengesFirstRoleIdConfigurationKey);
    private readonly ulong _secondRoleId = config.GetValue<ulong>(ConfigKeys.DailyChallengesSecondRoleIdConfigurationKey);
    private readonly ulong _thirdRoleId = config.GetValue<ulong>(ConfigKeys.DailyChallengesThirdRoleIdConfigurationKey);

    public async Task<Unit> Handle(DistributeDailyChallengeRolesCommand request, CancellationToken cancellationToken)
    {
        await discordServerRolesAccess.RemoveRoleFromAllPlayersAsync(_firstRoleId).ConfigureAwait(false);
        await discordServerRolesAccess.RemoveRoleFromAllPlayersAsync(_secondRoleId).ConfigureAwait(false);
        await discordServerRolesAccess.RemoveRoleFromAllPlayersAsync(_thirdRoleId).ConfigureAwait(false);

        var firstPlayersUserIds = new HashSet<string>();
        var secondPlayersUserIds = new HashSet<string>();
        var thirdPlayersUserIds = new HashSet<string>();

        var rolePriorityGroupedResults = request.Results
            .GroupBy(r => r.RolePriority)
            .OrderByDescending(x => x.Key);

        foreach (var rolePriorityGroup in rolePriorityGroupedResults)
        {
            var cleanedResults = rolePriorityGroup
                .Select(r => r.Players
                    .Where(p => !firstPlayersUserIds.Contains(p.Nickname) &&
                                !secondPlayersUserIds.Contains(p.Nickname) &&
                                !thirdPlayersUserIds.Contains(p.Nickname))
                    .ToList())
                .ToList();

            foreach (var cleanedResult in cleanedResults)
            {
                var place = 1;
                foreach (var player in cleanedResult)
                {
                    switch (place++)
                    {
                        case 1: firstPlayersUserIds.Add(player.UserId); break;
                        case 2: secondPlayersUserIds.Add(player.UserId); break;
                        case 3: thirdPlayersUserIds.Add(player.UserId); break;
                    }
                }
            }
        }

        var firstPlayers = await mediator.Send(new GeoGuessrUserIdsToDiscordUserIdsQuery(firstPlayersUserIds), cancellationToken).ConfigureAwait(false);
        var secondPlayers = await mediator.Send(new GeoGuessrUserIdsToDiscordUserIdsQuery(secondPlayersUserIds), cancellationToken).ConfigureAwait(false);
        var thirdPlayers = await mediator.Send(new GeoGuessrUserIdsToDiscordUserIdsQuery(thirdPlayersUserIds), cancellationToken).ConfigureAwait(false);

        await discordServerRolesAccess.AddRoleToMembersByUserIdsAsync(firstPlayers, _firstRoleId).ConfigureAwait(false);
        await discordServerRolesAccess.AddRoleToMembersByUserIdsAsync(secondPlayers, _secondRoleId).ConfigureAwait(false);
        await discordServerRolesAccess.AddRoleToMembersByUserIdsAsync(thirdPlayers, _thirdRoleId).ConfigureAwait(false);

        return Unit.Value;
    }
}
