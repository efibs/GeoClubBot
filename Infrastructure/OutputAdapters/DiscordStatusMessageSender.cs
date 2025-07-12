using System.Text;
using Discord.WebSocket;
using Entities;
using GeoClubBot;
using Microsoft.Extensions.Configuration;
using UseCases.OutputPorts;

namespace Infrastructure.OutputAdapters;

public class DiscordStatusMessageSender(DiscordSocketClient client, IConfiguration config) : IStatusMessageSender
{
    public async Task SendActivityStatusUpdateMessageAsync(List<GeoGuessrClubMemberActivityStatus> statuses)
    {
        // Get the server
        var server = client.GetGuild(_guildId);

        // Sanity check
        if (server == null)
        {
            throw new InvalidOperationException($"No server found for id {_guildId}");
        }

        // Get the channel
        var channel = server.GetTextChannel(_channelId);

        // Sanity check
        if (channel == null)
        {
            throw new InvalidOperationException($"No channel found for id {_channelId}");
        }

        // Build the message
        var messageString = _buildStatusUpdateMessageString(statuses);

        // Send the message
        await channel.SendMessageAsync(messageString).ConfigureAwait(false);
    }

    private string _buildStatusUpdateMessageString(List<GeoGuessrClubMemberActivityStatus> statuses)
    {
        // Create a string builder
        var builder = new StringBuilder("**======= Activity status update =======**\n\n");

        // Add header for members that failed to meet the requirement
        builder.Append("----- Members that failed to meet the ");
        builder.Append(_requirement);
        builder.Append("XP requirement -----");

        // Get the players that failed to meet the requirement
        var playersWithFailedRequirement = statuses
            .Where(s => !s.TargetAchieved)
            .OrderByDescending(s => s.NumStrikes)
            .ToList();

        // If no player failed the requirement
        if (playersWithFailedRequirement.Count == 0)
        {
            builder.Append("\n```diff\n+ None :)\n```");
        }

        // For every player that failed the requirement
        foreach (var player in playersWithFailedRequirement)
        {
            // Add a new line
            builder.AppendLine();

            // If the player is out of strikes
            if (player.IsOutOfStrikes)
            {
                // Append player in red text
                builder.Append("* ```diff\n- ");
                builder.Append(player.Nickname);
                builder.Append(" got only ");
                builder.Append(player.XpSinceLastUpdate);
                builder.Append("XP and already had ");
                builder.Append(player.NumStrikes - 1);
                builder.Append(" strikes and therefore needs to be kicked.\n```");
            }
            else
            {
                // Append player in regular text
                builder.Append("* ");
                builder.Append(player.Nickname);
                builder.Append(" got only ");
                builder.Append(player.XpSinceLastUpdate);
                builder.Append("XP and therefore is now on ");
                builder.Append(player.NumStrikes);
                builder.Append(" strikes.");
            }
        }

        return builder.ToString();
    }

    private readonly ulong _guildId = config.GetValue<ulong>(ConfigKeys.ActivityCheckerMainServerIdConfigurationKey);
    private readonly ulong _channelId = config.GetValue<ulong>(ConfigKeys.ActivityCheckerTextChannelIdConfigurationKey);
    private readonly int _requirement = config.GetValue<int>(ConfigKeys.ActivityCheckerMinXpConfigurationKey);
}