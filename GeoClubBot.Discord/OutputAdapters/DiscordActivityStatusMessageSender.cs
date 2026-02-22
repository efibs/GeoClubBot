using System.Text;
using Configuration;
using Discord.WebSocket;
using Entities;
using Microsoft.Extensions.Options;
using UseCases.OutputPorts;

namespace GeoClubBot.Discord.OutputAdapters;

public class DiscordActivityStatusMessageSender(DiscordSocketClient client, IOptions<DiscordConfiguration> discordConfig, IOptions<ActivityCheckerConfiguration> activityCheckerConfig) : IActivityStatusMessageSender
{
    private const int MaxNumPlayersPerMessage = 15;

    public async Task SendActivityStatusUpdateMessageAsync(List<ClubMemberActivityStatus> statuses, string clubName, int minXP)
    {
        // Get the server
        var server = client.GetGuild(discordConfig.Value.ServerId);

        // Sanity check
        if (server == null)
        {
            throw new InvalidOperationException($"No server found for id {discordConfig.Value.ServerId}");
        }

        // Get the channel
        var channel = server.GetTextChannel(activityCheckerConfig.Value.TextChannelId);

        // Sanity check
        if (channel == null)
        {
            throw new InvalidOperationException($"No channel found for id {activityCheckerConfig.Value.TextChannelId}");
        }

        // Get the players that failed to meet the requirement
        var playersWithFailedRequirement = statuses
            .Where(s => s.TargetAchieved == false)
            .OrderByDescending(s => s.NumStrikes)
            .ThenBy(s => s.XpSinceLastUpdate)
            .ToList();

        // Build the message first part of the message
        var messageString =
            _buildStatusUpdateMessageBeginningString(
                playersWithFailedRequirement.Take(MaxNumPlayersPerMessage).ToList(), clubName, minXP);

        // Send the message
        await channel.SendMessageAsync(messageString).ConfigureAwait(false);

        // While there are still players to be processed
        var skipCount = MaxNumPlayersPerMessage;
        while (playersWithFailedRequirement.Skip(skipCount).Any())
        {
            // Create a string builder
            var builder = new StringBuilder();

            // Get the players for this chunk
            var playersChunk = playersWithFailedRequirement.Skip(skipCount).Take(MaxNumPlayersPerMessage).ToList();

            // Append the players to the string builder
            _appendPlayers(builder, playersChunk);

            // Send the message
            await channel.SendMessageAsync(builder.ToString()).ConfigureAwait(false);

            // Increase skip count
            skipCount += MaxNumPlayersPerMessage;
        }
        
        // Get the players with individual targets
        var playersWithIndividualTargets = statuses
            .Where(s => string.IsNullOrWhiteSpace(s.IndividualTargetReason) == false)
            .ToList();
        
        // If there are players with individual targets
        if (playersWithIndividualTargets.Any())
        {
            // Build the messages for the players with individual targets
            var playersWithIndividualTargetsMessage = _buildIndividualTargetPlayersMessage(playersWithIndividualTargets);
            
            // Send the message
            await channel.SendMessageAsync(playersWithIndividualTargetsMessage).ConfigureAwait(false);
        }
    }

    private string _buildStatusUpdateMessageBeginningString(List<ClubMemberActivityStatus> players, string clubName, int minXP)
    {
        // Create a string builder
        var builder = new StringBuilder($"**======= Activity status update - {clubName} =======**\n\n");

        // Add header for members that failed to meet the requirement
        builder.Append("Members that failed to meet the ");
        builder.Append(minXP);
        builder.Append("XP requirement:");

        // If no player failed the requirement
        if (!players.Any())
        {
            builder.Append("\n```ansi\n\e[2;31m\e[0m\e[2;32mNone :)\e[0m\n```");
        }

        // Append the players
        _appendPlayers(builder, players);

        return builder.ToString();
    }

    private void _appendPlayers(StringBuilder builder, List<ClubMemberActivityStatus> players)
    {
        // For every player that failed the requirement
        foreach (var player in players)
        {
            // Add a new line
            builder.AppendLine();

            // If the player is out of strikes
            if (player.IsOutOfStrikes)
            {
                // Append player in red text
                builder.Append("```ansi\n\e[2;31m");
                builder.Append(player.Nickname);
                builder.Append("\e[0m got only ");
                builder.Append(player.XpSinceLastUpdate);
                builder.Append("XP");
                if (player.IndividualTargetReason != null)
                {
                    builder.Append(" (individual target: ");
                    builder.Append(player.IndividualTarget);
                    builder.Append("XP - ");
                    builder.Append(player.IndividualTargetReason);
                    builder.Append(")");
                }
                builder.Append(" and already had ");
                builder.Append(player.NumStrikes - 1);
                builder.Append(" strikes and therefore \e[2;31mneeds to be kicked\e[0m.\n```");
            }
            else
            {
                // Append player in regular text
                builder.Append("* ");
                builder.Append(player.Nickname);
                builder.Append(" got only ");
                builder.Append(player.XpSinceLastUpdate);
                builder.Append("XP");
                if (player.IndividualTargetReason != null)
                {
                    builder.Append(" (individual target: ");
                    builder.Append(player.IndividualTarget);
                    builder.Append("XP - ");
                    builder.Append(player.IndividualTargetReason);
                    builder.Append(")");
                }
                builder.Append(" and therefore is now on ");
                builder.Append(player.NumStrikes);
                builder.Append(" strikes.");
            }
        }
    }

    private string _buildIndividualTargetPlayersMessage(List<ClubMemberActivityStatus> playersWithIndividualTarget)
    {
        // Create the builder
        var builder = new StringBuilder("​\nPlayers that have an individual target:");
        
        // For every excused player
        foreach (var player in playersWithIndividualTarget)
        {
            builder.AppendLine();
            builder.Append("* ");
            builder.Append(player.Nickname);
            builder.Append(" - individual target: ");
            builder.Append(player.IndividualTarget);
            builder.Append("XP; Reason(s): ");
            builder.Append(player.IndividualTargetReason);
        }
        
        return builder.ToString();
    }
}