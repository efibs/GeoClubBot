using Discord.WebSocket;
using UseCases.OutputPorts;

namespace Infrastructure.OutputAdapters;

public class DiscordStatusUpdater(DiscordSocketClient client) : IStatusUpdater
{
    public async Task UpdateStatusAsync(string newStatus)
    {
        // Set the status
        await client.SetCustomStatusAsync(newStatus);
    }
}