using Configuration;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.OutputPorts.Discord;

namespace GeoClubBot.Discord.OutputAdapters;

public partial class DiscordDiscordServerRolesAccess(DiscordSocketClient client, ILogger<DiscordDiscordServerRolesAccess> logger, IOptions<DiscordConfiguration> config) : IDiscordServerRolesAccess
{
    public async Task<int> RemoveRoleFromAllPlayersAsync(ulong roleId)
    {
        // Get the guild
        var guild = client.GetGuild(config.Value.ServerId);
        
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
        var guild = client.GetGuild(config.Value.ServerId);
        
        // Get the user
        var user = guild.GetUser(userId);
        
        // Remove all the roles from the user
        await user.RemoveRolesAsync(roleIds).ConfigureAwait(false);
    }

    public async Task RemoveRoleFromPlayersAsync(IEnumerable<ulong> userIds, ulong roleId)
    {
        // Get the guild
        var guild = client.GetGuild(config.Value.ServerId);
        
        // Remove the role from each user
        foreach (var userId in userIds)
        {
            // Get the user
            var user =  guild.GetUser(userId);
            
            // Remove the role
            await user.RemoveRoleAsync(roleId).ConfigureAwait(false);
        }
    }

    public async Task AddRoleToMembersByUserIdsAsync(IEnumerable<ulong> userIds, ulong roleId)
    {
        // Get the guild
        var guild = client.GetGuild(config.Value.ServerId);

        foreach (var userId in userIds)
        {
            // Get the user
            var user = guild.GetUser(userId);
            
            // Add the role to the user
            await user.AddRoleAsync(roleId).ConfigureAwait(false);
            
            // Log debug
            LogAddedRoleToMember(logger, roleId, user.DisplayName);
        }
    }

    public Task<List<ulong>> ReadMembersWithRoleAsync(ulong roleId)
    {
        // Get the guild
        var guild = client.GetGuild(config.Value.ServerId);
        
        // Get the users with the role
        var usersWithRole = guild.Users
            .Where(u => u.Roles.Any(r => r.Id == roleId))
            .Select(u => u.Id)
            .ToList();

        return Task.FromResult(usersWithRole);
    }

    [LoggerMessage(LogLevel.Debug, "Added role {roleId} to member {userDisplayName}.")]
    static partial void LogAddedRoleToMember(ILogger<DiscordDiscordServerRolesAccess> logger, ulong roleId, string userDisplayName);
}