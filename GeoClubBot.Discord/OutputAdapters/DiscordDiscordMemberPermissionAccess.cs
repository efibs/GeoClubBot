using Configuration;
using Discord.WebSocket;
using Microsoft.Extensions.Options;
using UseCases.OutputPorts.Discord;

namespace GeoClubBot.Discord.OutputAdapters;

public class DiscordDiscordMemberPermissionAccess(
    DiscordSocketClient client,
    IOptions<DiscordConfiguration> config) : IDiscordMemberPermissionAccess
{
    public Task<bool> IsAdministratorAsync(ulong discordUserId, CancellationToken cancellationToken = default)
    {
        // The socket client caches guild members in-process (GuildMembers intent +
        // AlwaysDownloadUsers) and keeps them current via gateway events, so this is a lookup, not
        // an HTTP call. Null guild (gateway down) or unknown user both answer false — fail closed.
        var guild = client.GetGuild(config.Value.ServerId);
        var user = guild?.GetUser(discordUserId);

        return Task.FromResult(user?.GuildPermissions.Administrator == true);
    }

    public Task<bool> IsAdministratorOrHasAnyRoleAsync(ulong discordUserId, IReadOnlyCollection<ulong> roleIds, CancellationToken cancellationToken = default)
    {
        var user = client.GetGuild(config.Value.ServerId)?.GetUser(discordUserId);
        if (user is null)
        {
            return Task.FromResult(false);
        }

        return Task.FromResult(user.GuildPermissions.Administrator || user.Roles.Any(role => roleIds.Contains(role.Id)));
    }
}
