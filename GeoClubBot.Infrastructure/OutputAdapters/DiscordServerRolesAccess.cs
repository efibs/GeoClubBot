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
            await user.RemoveRoleAsync(roleId);
        }
        
        return usersWithRole.Count;
    }

    public async Task AddRoleToMembersByNicknameAsync(HashSet<string> nicknames, ulong roleId)
    {
        // Get the guild
        var guild = client.GetGuild(_guildId);

        // Get the user of the nickname
        var users = guild.Users.Where(u => nicknames.Contains(u.DisplayName));

        foreach (var user in users)
        {
            // Add the role to the user
            await user.AddRoleAsync(roleId);
        
            // Log debug
            logger.LogDebug($"Added role {roleId} to member {user.DisplayName}.");
        }
    }
    
    private readonly ulong _guildId = config.GetValue<ulong>(ConfigKeys.DiscordServerIdConfigurationKey);
}