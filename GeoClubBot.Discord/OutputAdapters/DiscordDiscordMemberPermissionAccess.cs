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
}
