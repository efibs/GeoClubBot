using System.Text;
using Discord.WebSocket;
using Entities;
using GeoClubBot;
using Microsoft.Extensions.Configuration;
using UseCases.OutputPorts;

namespace Infrastructure.OutputAdapters;

public class DiscordStatusMessageSender(DiscordSocketClient client, IConfiguration config) : IStatusMessageSender
{
    private const int MaxNumPlayersPerMessage = 15;
    
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

        // Get the players that failed to meet the requirement
        var playersWithFailedRequirement = statuses
            .Where(s => !s.TargetAchieved)
            .OrderByDescending(s => s.NumStrikes)
            .ToList();
        
        // Build the message first part of the message
        var messageString = _buildStatusUpdateMessageBeginningString(playersWithFailedRequirement.Take(MaxNumPlayersPerMessage).ToList());

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
    }

    private string _buildStatusUpdateMessageBeginningString(List<GeoGuessrClubMemberActivityStatus> players)
    {
        // Create a string builder
        var builder = new StringBuilder("**======= Activity status update =======**\n\n");

        // Add header for members that failed to meet the requirement
        builder.Append("Members that failed to meet the ");
        builder.Append(_requirement);
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
    
    private void _appendPlayers(StringBuilder builder, List<GeoGuessrClubMemberActivityStatus> players)
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
                builder.Append("XP and already had ");
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
                builder.Append("XP and therefore is now on ");
                builder.Append(player.NumStrikes);
                builder.Append(" strikes.");
            }
        }
    }

    private readonly ulong _guildId = config.GetValue<ulong>(ConfigKeys.ActivityCheckerMainServerIdConfigurationKey);
    private readonly ulong _channelId = config.GetValue<ulong>(ConfigKeys.ActivityCheckerTextChannelIdConfigurationKey);
    private readonly int _requirement = config.GetValue<int>(ConfigKeys.ActivityCheckerMinXpConfigurationKey);
}