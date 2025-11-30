using Discord.WebSocket;

namespace GeoClubBot.Discord.Services;

public class DiscordBotReadyService
{
    public DiscordBotReadyService(DiscordSocketClient client)
    {
        // Attach the ready callback
        client.Ready += _onDiscordSocketClientReady;
    }

    public Task DiscordSocketClientReady => _discordSocketClientReadyCompletionSource.Task;

    private Task _onDiscordSocketClientReady()
    {
        _discordSocketClientReadyCompletionSource.SetResult();
        return Task.CompletedTask;
    }

    private readonly TaskCompletionSource _discordSocketClientReadyCompletionSource = new();
}