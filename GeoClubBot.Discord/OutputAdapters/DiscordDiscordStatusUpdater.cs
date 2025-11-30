using Discord.WebSocket;
using UseCases.OutputPorts;

namespace GeoClubBot.Discord.OutputAdapters;

public class DiscordDiscordStatusUpdater(DiscordSocketClient client) : IDiscordStatusUpdater
{
    public async Task UpdateStatusAsync(string newStatus)
    {
        // Set the status
        await client.SetCustomStatusAsync(newStatus).ConfigureAwait(false);
    }
}