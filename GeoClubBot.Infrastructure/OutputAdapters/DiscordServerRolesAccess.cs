using Constants;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using UseCases.OutputPorts;

namespace Infrastructure.OutputAdapters;

public class DiscordServerRolesAccess(DiscordSocketClient client, IConfiguration config, ILogger<DiscordServerRolesAccess> logger) : IServerRolesAccess
{
    public async Task<int> RemoveRoleFromAllPlayersAsync(ulong roleId)
    {
        // Get the guild
        var guild = client.GetGuild(_guildId);
        
        // Get the users with the role
        var usersWithRole = guild.Users
            .Where(u => u.Roles.Any(r => r.Id == roleId))
            .ToList();

        // Remove the role from each user
        foreach (var user in usersWithRole)
        {
            await user.RemoveRoleAsync(roleId).ConfigureAwait(false);
        }
        
        return usersWithRole.Count;
    }

    public async Task RemoveRolesFromUserAsync(ulong userId, IEnumerable<ulong> roleIds)
    {
        // Get the guild
        var guild = client.GetGuild(_guildId);
        
        // Get the user
        var user = guild.GetUser(userId);
        
        // Remove all the roles from the user
        await user.RemoveRolesAsync(roleIds).ConfigureAwait(false);
    }

    public async Task AddRoleToMembersByUserIdsAsync(IEnumerable<ulong> userIds, ulong roleId)
    {
        // Get the guild
        var guild = client.GetGuild(_guildId);

        foreach (var userId in userIds)
        {
            // Get the user
            var user = guild.GetUser(userId);
            
            // Add the role to the user
            await user.AddRoleAsync(roleId).ConfigureAwait(false);
        
            // Log debug
            logger.LogDebug($"Added role {roleId} to member {user.DisplayName}.");
        }
    }
    
    private readonly ulong _guildId = config.GetValue<ulong>(ConfigKeys.DiscordServerIdConfigurationKey);
}