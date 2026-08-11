using Configuration;
using Entities;
using MediatR;
using Microsoft.Extensions.Options;
using UseCases.Abstractions;
using UseCases.OutputPorts.Discord;
using UseCases.UseCases.Users;

namespace UseCases.UseCases.DailyChallenge;

public sealed record DistributeDailyChallengeRolesCommand(List<ClubChallengeResult> Results) : ICommand;

public sealed class DistributeDailyChallengeRolesHandler(
    ISender mediator,
    IDiscordServerRolesAccess discordServerRolesAccess,
    IOptions<DailyChallengesConfiguration> dailyChallengesOptions) : IRequestHandler<DistributeDailyChallengeRolesCommand, Unit>
{
    private readonly ulong _firstRoleId = dailyChallengesOptions.Value.FirstRoleId;
    private readonly ulong _secondRoleId = dailyChallengesOptions.Value.SecondRoleId;
    private readonly ulong _thirdRoleId = dailyChallengesOptions.Value.ThirdRoleId;

    public async Task<Unit> Handle(DistributeDailyChallengeRolesCommand request, CancellationToken cancellationToken)
    {
        await discordServerRolesAccess.RemoveRoleFromAllPlayersAsync(_firstRoleId, cancellationToken).ConfigureAwait(false);
        await discordServerRolesAccess.RemoveRoleFromAllPlayersAsync(_secondRoleId, cancellationToken).ConfigureAwait(false);
        await discordServerRolesAccess.RemoveRoleFromAllPlayersAsync(_thirdRoleId, cancellationToken).ConfigureAwait(false);

        var firstPlayersUserIds = new List<string>();
        var secondPlayersUserIds = new List<string>();
        var thirdPlayersUserIds = new List<string>();
        var awardedUserIds = new HashSet<string>(StringComparer.Ordinal);

        // Highest role priority first: every player earns at most one podium role, so a player that
        // already placed in a higher-priority challenge is skipped here and the players behind them
        // move up a place. Matching is by user id — nicknames are neither stable nor unique.
        foreach (var result in request.Results.OrderByDescending(r => r.RolePriority))
        {
            var place = 1;
            foreach (var player in result.Players)
            {
                if (!awardedUserIds.Add(player.UserId))
                {
                    continue;
                }

                switch (place++)
                {
                    case 1: firstPlayersUserIds.Add(player.UserId); break;
                    case 2: secondPlayersUserIds.Add(player.UserId); break;
                    case 3: thirdPlayersUserIds.Add(player.UserId); break;
                }

                if (place > 3)
                {
                    break;
                }
            }
        }

        var firstPlayers = await mediator.Send(new GeoGuessrUserIdsToDiscordUserIdsQuery(firstPlayersUserIds), cancellationToken).ConfigureAwait(false);
        var secondPlayers = await mediator.Send(new GeoGuessrUserIdsToDiscordUserIdsQuery(secondPlayersUserIds), cancellationToken).ConfigureAwait(false);
        var thirdPlayers = await mediator.Send(new GeoGuessrUserIdsToDiscordUserIdsQuery(thirdPlayersUserIds), cancellationToken).ConfigureAwait(false);

        await discordServerRolesAccess.AddRoleToMembersByUserIdsAsync(firstPlayers, _firstRoleId, cancellationToken).ConfigureAwait(false);
        await discordServerRolesAccess.AddRoleToMembersByUserIdsAsync(secondPlayers, _secondRoleId, cancellationToken).ConfigureAwait(false);
        await discordServerRolesAccess.AddRoleToMembersByUserIdsAsync(thirdPlayers, _thirdRoleId, cancellationToken).ConfigureAwait(false);

        return Unit.Value;
    }
}
