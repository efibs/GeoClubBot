using Constants;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using UseCases.OutputPorts;

namespace Infrastructure.OutputAdapters;

public class DiscordMessageSender(DiscordSocketClient client, IConfiguration config) : IMessageSender
{
    public async Task SendMessageAsync(string message, string channelId)
    {
        // Parse channel id to ulong
        var channelIdUlong = ulong.Parse(channelId);
        
        // Get the server
        var server = client.GetGuild(_guildId);
        
        // Sanity check
        if (server == null)
        {
            throw new InvalidOperationException($"No server found for id {_guildId}");
        }

        // Get the channel
        var channel = server.GetTextChannel(channelIdUlong);

        // Sanity check
        if (channel == null)
        {
            throw new InvalidOperationException($"No channel found for id {channelIdUlong}");
        }
        
        // Send the message
        await channel.SendMessageAsync(message).ConfigureAwait(false);
    }
    
    private readonly ulong _guildId = config.GetValue<ulong>(ConfigKeys.DiscordServerIdConfigurationKey);
}