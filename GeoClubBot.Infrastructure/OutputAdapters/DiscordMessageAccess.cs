using System.Text;
using Constants;
using Discord;
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
        
        // Build the message
        var msgBuilder = new StringBuilder("# Select the roles you would like to have\nThe available roles are:\n");
        
        // For every setting
        foreach (var roleSetting in selfRoleSettings)
        {
            // Get the role name
            var role = await server.GetRoleAsync(roleSetting.RoleId).ConfigureAwait(false);

            // If the role has an icon set
            if (string.IsNullOrWhiteSpace(roleSetting.RoleEmoji) == false)
            {
                msgBuilder.Append(roleSetting.RoleEmoji);
            }
            else
            {
                msgBuilder.Append("\t  ");
            }

            msgBuilder.Append(' ');
            msgBuilder.Append(role.Name);
            
            // If the role has a description set
            if (string.IsNullOrWhiteSpace(roleSetting.RoleDescription) == false)
            {
                msgBuilder.Append(": ");
                msgBuilder.Append(roleSetting.RoleDescription);
            }

            msgBuilder.AppendLine();
        }
        
        // Build the message
        var msg = msgBuilder.ToString();
        
        // Build the button component
        var button = new ComponentBuilder()
            .WithButton("Select roles", customId: ComponentIds.SelfRolesSelectButtonId)
            .Build();
        
        // Send the message
        await channel
            .SendMessageAsync(msg, components: button)
            .ConfigureAwait(false);
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