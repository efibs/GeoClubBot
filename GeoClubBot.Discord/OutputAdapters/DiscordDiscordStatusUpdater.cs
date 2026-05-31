using Discord.WebSocket;
using UseCases.OutputPorts.Discord;

namespace GeoClubBot.Discord.OutputAdapters;

public class DiscordDiscordStatusUpdater(DiscordSocketClient client) : IDiscordStatusUpdater
{
    public async Task UpdateStatusAsync(string newStatus, CancellationToken cancellationToken = default)
    {
        // Set the status
        await client.SetCustomStatusAsync(newStatus).ConfigureAwait(false);
    }
}