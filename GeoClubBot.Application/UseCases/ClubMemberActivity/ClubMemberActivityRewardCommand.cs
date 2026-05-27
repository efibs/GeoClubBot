using System.Text;
using Configuration;
using Entities;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.Abstractions;
using UseCases.OutputPorts.Discord;
using UseCases.UseCases.Users;

namespace UseCases.UseCases.ClubMemberActivity;

public sealed record ClubMemberActivityRewardCommand(List<ClubMemberActivityStatus> Statuses) : ICommand;

public sealed class ClubMemberActivityRewardHandler(
    ISender mediator,
    IDiscordServerRolesAccess discordServerRolesAccess,
    IDiscordMessageAccess discordMessageAccess,
    ILogger<ClubMemberActivityRewardHandler> logger,
    IOptions<ActivityRewardConfiguration> config) : IRequestHandler<ClubMemberActivityRewardCommand, Unit>
{
    public async Task<Unit> Handle(ClubMemberActivityRewardCommand request, CancellationToken cancellationToken)
    {
        logger.LogDebug("Starting reward member activity");

        var leaderboard = request.Statuses
            .Where(s => s.XpSinceLastUpdate > 0)
            .GroupBy(s => s.XpSinceLastUpdate)
            .OrderByDescending(g => g.Key)
            .Take(3)
            .ToList();

        var mvpPlayerUserIds = await MentionMvpsAsync(leaderboard, cancellationToken).ConfigureAwait(false);

        await UpdateRolesAsync(mvpPlayerUserIds, cancellationToken).ConfigureAwait(false);

        return Unit.Value;
    }

    private async Task<List<string>> MentionMvpsAsync(List<IGrouping<int, ClubMemberActivityStatus>> leaderboard, CancellationToken cancellationToken)
    {
        IEnumerable<string> mvpPlayerUserIds = [];

        try
        {
            if (leaderboard.Count == 0)
            {
                return [];
            }

            var msgBuilder = new StringBuilder("# The new MVP's of the club are here! :partying_face:\n");
            msgBuilder.AppendLine("The club members that achieved the most club XP since last time are:");

            var place = 1;
            foreach (var group in leaderboard)
            {
                msgBuilder.Append("**#");
                msgBuilder.Append(place);
                msgBuilder.Append(":** With ");
                msgBuilder.Append(group.Key);
                msgBuilder.Append("XP: ");
                msgBuilder.Append(string.Join(", ", group.Select(s => s.Nickname)));

                if (place == 1)
                {
                    msgBuilder.Append(group.Count() > 1
                        ? " (Our new MVP's) :clap: "
                        : " (Our new MVP) :clap: ");

                    mvpPlayerUserIds = group.Select(s => s.UserId);
                }

                msgBuilder.AppendLine();
                place++;
            }

            await discordMessageAccess
                .SendMessageAsync(msgBuilder.ToString(), config.Value.TextChannelId, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to mention MVPs.");
        }

        return mvpPlayerUserIds.ToList();
    }

    private async Task UpdateRolesAsync(IEnumerable<string> mvpPlayerUserIds, CancellationToken cancellationToken)
    {
        var discordUserIds = await mediator
            .Send(new GeoGuessrUserIdsToDiscordUserIdsQuery(mvpPlayerUserIds), cancellationToken)
            .ConfigureAwait(false);

        var membersWithMvpRole = await discordServerRolesAccess
            .ReadMembersWithRoleAsync(config.Value.MvpRoleId, cancellationToken)
            .ConfigureAwait(false);

        var membersToAddMvpRole = discordUserIds.Except(membersWithMvpRole);
        var membersToRemoveMvpRole = membersWithMvpRole.Except(discordUserIds);

        await discordServerRolesAccess
            .AddRoleToMembersByUserIdsAsync(membersToAddMvpRole, config.Value.MvpRoleId, cancellationToken)
            .ConfigureAwait(false);
        await discordServerRolesAccess
            .RemoveRoleFromPlayersAsync(membersToRemoveMvpRole, config.Value.MvpRoleId, cancellationToken)
            .ConfigureAwait(false);
    }
}
