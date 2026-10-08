using Configuration;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Options;
using UseCases.OutputPorts.Discord;

namespace GeoClubBot.Discord.OutputAdapters;

public class DiscordDiscordServerStatsAccess(
    DiscordSocketClient client,
    IOptions<DiscordConfiguration> config) : IDiscordServerStatsAccess
{
    public async Task<int> ReadApproximateOnlineCountAsync(CancellationToken cancellationToken = default)
    {
        // The gateway's member cache knows no presences (the privileged GuildPresences intent is not
        // requested), but the REST guild with counts carries Discord's own approximate count.
        var serverId = config.Value.ServerId;
        var guild = await client.Rest
            .GetGuildAsync(serverId, withCounts: true, new RequestOptions { CancelToken = cancellationToken })
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Discord server {serverId} was not found.");

        return guild.ApproximatePresenceCount
               ?? throw new InvalidOperationException($"Discord reported no online count for server {serverId}.");
    }
}
