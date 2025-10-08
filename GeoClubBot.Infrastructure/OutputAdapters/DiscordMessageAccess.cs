using Constants;
using Discord.WebSocket;
using Entities;
using Microsoft.Extensions.Configuration;
using UseCases.OutputPorts;

namespace Infrastructure.OutputAdapters;

public class DiscordMessageAccess(DiscordSocketClient client, IConfiguration config) : IMessageAccess
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

    public async Task SendSelfRolesMessageAsync(ulong channelId, IEnumerable<SelfRoleSetting> selfRoleSettings)
    {
        // Get the server
        var server = client.GetGuild(_guildId);
        
        // Sanity check
        if (server == null)
        {
            throw new InvalidOperationException($"No server found for id {_guildId}");
        }

        // Get the channel
        var channel = server.GetTextChannel(channelId);

        // Sanity check
        if (channel == null)
        {
            throw new InvalidOperationException($"No channel found for id {channelId}");
        }
        
        // Send the message
        await channel.SendMessageAsync(TODO).ConfigureAwait(false);
    }

    public async Task DeleteMessageAsync(ulong messageId, ulong channelId)
    {
        // Get the server
        var server = client.GetGuild(_guildId);
        
        // Sanity check
        if (server == null)
        {
            throw new InvalidOperationException($"No server found for id {_guildId}");
        }
        
        // Get the channel
        var channel = server.GetTextChannel(channelId);
        
        // Sanity check
        if (channel == null)
        {
            throw new InvalidOperationException($"No channel found for id {channelId}");
        }
        
        // Get the message
        var message = await channel.GetMessageAsync(messageId).ConfigureAwait(false);
        
        // Sanity check
        if (channel == null)
        {
            throw new InvalidOperationException($"No message found for id {messageId} in channel {channelId}");
        }
        
        // Delete the message
        await message.DeleteAsync().ConfigureAwait(false);
    }

    private readonly ulong _guildId = config.GetValue<ulong>(ConfigKeys.DiscordServerIdConfigurationKey);
}