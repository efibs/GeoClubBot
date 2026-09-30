using MediatR;
using UseCases.Abstractions;
using UseCases.OutputPorts.Discord;
using UseCases.UseCases.Users;

namespace UseCases.UseCases.CountryChallenges;

/// <summary>
/// Takes the roles of <see cref="RoleAssignment"/> from everyone who holds them and hands them to the
/// players it names. Players without a linked Discord account cannot be given a role and are skipped.
/// </summary>
public sealed record DistributeCountryChallengeRolesCommand(RoleAssignment Assignment) : ICommand;

public sealed class DistributeCountryChallengeRolesHandler(
    ISender mediator,
    IDiscordServerRolesAccess discordServerRolesAccess)
    : IRequestHandler<DistributeCountryChallengeRolesCommand, Unit>
{
    public async Task<Unit> Handle(DistributeCountryChallengeRolesCommand request, CancellationToken cancellationToken)
    {
        var assignment = request.Assignment;

        // One role may cover several places ("top three"), so each is taken away once, before any is given.
        foreach (var roleId in assignment.RoleIdsByPlace.Distinct())
        {
            await discordServerRolesAccess.RemoveRoleFromAllPlayersAsync(roleId, cancellationToken).ConfigureAwait(false);
        }

        for (var place = 0; place < assignment.RoleIdsByPlace.Count && place < assignment.PlayersByPlace.Count; place++)
        {
            var players = assignment.PlayersByPlace[place];
            if (players.Count == 0)
            {
                continue;
            }

            var discordUserIds = await mediator
                .Send(new GeoGuessrUserIdsToDiscordUserIdsQuery(players), cancellationToken)
                .ConfigureAwait(false);

            await discordServerRolesAccess
                .AddRoleToMembersByUserIdsAsync(discordUserIds, assignment.RoleIdsByPlace[place], cancellationToken)
                .ConfigureAwait(false);
        }

        return Unit.Value;
    }
}
