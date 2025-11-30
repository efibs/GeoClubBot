using System.Text;
using Configuration;
using Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.InputPorts.ClubMemberActivity;
using UseCases.InputPorts.Users;
using UseCases.OutputPorts.Discord;

namespace UseCases.UseCases.ClubMemberActivity;

public class ClubMemberActivityRewardUseCase(IGeoGuessrUserIdsToDiscordUserIdsUseCase geoGuessrUserIdsToDiscordUserIdsUseCase,
    IDiscordServerRolesAccess discordServerRolesAccess, 
    IDiscordMessageAccess discordMessageAccess, 
    ILogger<ClubMemberActivityRewardUseCase> logger,
    IOptions<ActivityRewardConfiguration> config) : IClubMemberActivityRewardUseCase
{
    public async Task RewardMemberActivityAsync(List<ClubMemberActivityStatus> statuses)
    {
        logger.LogDebug("Starting reward member activity");
        
        // Group statuses by xp and then sort by xp to get the leaderboard
        var leaderboard = statuses
            .Where(s => s.XpSinceLastUpdate > 0)
            .GroupBy(s => s.XpSinceLastUpdate)
            .OrderByDescending(g => g.Key)
            .Take(3)
            .ToList();
        
        // Send the mention
        var mvpPlayerUserIds = await _mentionMvpsAsync(leaderboard).ConfigureAwait(false);
        
        // Update the roles
        await _updateRolesAsync(mvpPlayerUserIds).ConfigureAwait(false);
    }

    private async Task<List<string>> _mentionMvpsAsync(List<IGrouping<int, ClubMemberActivityStatus>> leaderboard)
    {
        IEnumerable<string> mvpPlayerUserIds = [];
        
        try
        {
            // If there are no players to mention
            if (leaderboard.Count == 0)
            {
                return [];
            }

            // The builder for the message
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

            // Build the message
            var msg = msgBuilder.ToString();

            // Send the message
            await discordMessageAccess.SendMessageAsync(msg, config.Value.TextChannelId).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to mention MVPs.");
        }

        return mvpPlayerUserIds.ToList();
    }

    private async Task _updateRolesAsync(IEnumerable<string> mvpPlayerUserIds)
    {
        // Get the discord user ids for the mvps
        var discordUserIds = await geoGuessrUserIdsToDiscordUserIdsUseCase.GetDiscordUserIdsAsync(mvpPlayerUserIds).ConfigureAwait(false);
        
        // Get the members that already have the mvp role
        var membersWithMvpRole = await discordServerRolesAccess.ReadMembersWithRoleAsync(config.Value.MvpRoleId).ConfigureAwait(false);
        
        // Get the members that need the mvp role
        var membersToAddMvpRole = discordUserIds.Except(membersWithMvpRole);
        
        // Get the members that are no longer mvp
        var membersToRemoveMvpRole = membersWithMvpRole.Except(discordUserIds);
        
        // Distribute the roles
        await discordServerRolesAccess.AddRoleToMembersByUserIdsAsync(membersToAddMvpRole, config.Value.MvpRoleId).ConfigureAwait(false);
        
        // Remove the roles
        await discordServerRolesAccess.RemoveRoleFromPlayersAsync(membersToRemoveMvpRole, config.Value.MvpRoleId).ConfigureAwait(false);
    }
}