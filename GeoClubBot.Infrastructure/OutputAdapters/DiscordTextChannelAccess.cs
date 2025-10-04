using Constants;
using Discord;
using Discord.WebSocket;
using Entities;
using Microsoft.Extensions.Configuration;
using UseCases.OutputPorts;

namespace Infrastructure.OutputAdapters;

public class DiscordTextChannelAccess(DiscordSocketClient client, IConfiguration config) : ITextChannelAccess
{
    public async Task<ulong?> CreatePrivateTextChannelAsync(ulong categoryId, string name, string description,
        IEnumerable<ulong>? allowedDiscordUserIds, IEnumerable<ulong>? allowedRoleIds)
    {
        // Get the guild
        var guild = client.GetGuild(_guildId);
        
        // Create the text channel
        var createdTextChannel = await guild.CreateTextChannelAsync(name, options =>
        {
            // Set to text channel
            options.ChannelType = ChannelType.Text;
            
            // Set the category id
            options.CategoryId = categoryId;
            
            // Set the description
            options.Topic = description;
            
            // Get the overwrites
            var overwrites = _getOverwrites(guild, allowedDiscordUserIds, allowedRoleIds);
            
            // Set to private
            options.PermissionOverwrites = overwrites;
        }).ConfigureAwait(false);
        
        return createdTextChannel?.Id;
    }

    public async Task UpdateTextChannelAsync(TextChannel newTextChannel)
    {
        // Get the guild
        var guild = client.GetGuild(_guildId);
        
        // Get the text channel
        var textChannel = guild.GetTextChannel(newTextChannel.Id);
        
        // Update the text channel
        await textChannel.ModifyAsync(options =>
        {
            // If a name is given
            if (string.IsNullOrWhiteSpace(newTextChannel.Name) == false)
            {
                // Update the name
                options.Name = newTextChannel.Name;
            }
            
            // If a description is given
            if (string.IsNullOrWhiteSpace(newTextChannel.Description) == false)
            {
                // Update the topic
                options.Topic = newTextChannel.Description;
            }
        }).ConfigureAwait(false);
    }
    
    public async Task<bool> DeleteTextChannelAsync(ulong textChannelId)
    {
        // Get the guild
        var guild = client.GetGuild(_guildId);
        
        // Get the text channel
        var textChannel = guild.GetTextChannel(textChannelId);
        
        // If the text channel was not found
        if (textChannel == null)
        {
            // Nothing to do
            return false;
        }
        
        // Delete the text channel
        await textChannel.DeleteAsync().ConfigureAwait(false);
        
        return true;
    }

    private List<Overwrite> _getOverwrites(SocketGuild guild, IEnumerable<ulong>? allowedDiscordUserIds, IEnumerable<ulong>? allowedRoleIds)
    {
        // Get the overwrites for the allowed users
        var allowedUsersOverwrites = allowedDiscordUserIds?
            .Select(uId => new Overwrite(uId, 
                PermissionTarget.User, 
                new OverwritePermissions(viewChannel: PermValue.Allow)))
            .ToList() ?? [];
            
        // Get the allowed role overwrites
        var allowedRolesOverwrites = allowedRoleIds?
            .Select(rId => new Overwrite(rId, 
                PermissionTarget.Role, 
                new OverwritePermissions(viewChannel: PermValue.Allow)))
            .ToList() ?? [];
            
        // Get the final overwrite
        var textChannelOverwrites = new List<Overwrite>
        {
            new(guild.EveryoneRole.Id, PermissionTarget.Role,
                new OverwritePermissions(viewChannel: PermValue.Deny)),
            new()
        };
            
        // Append the user overwrites
        textChannelOverwrites.AddRange(allowedUsersOverwrites);
            
        // Append the role overwrites
        textChannelOverwrites.AddRange(allowedRolesOverwrites);
        
        return textChannelOverwrites;
    }
    
    private readonly ulong _guildId = config.GetValue<ulong>(ConfigKeys.DiscordServerIdConfigurationKey);
}