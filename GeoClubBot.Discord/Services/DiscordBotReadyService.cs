using Discord.WebSocket;

namespace GeoClubBot.Discord.Services;

public class DiscordBotReadyService
{
    public DiscordBotReadyService(DiscordSocketClient client)
    {
        // Attach the ready callback
        client.Ready += OnDiscordSocketClientReady;
    }

    public Task DiscordSocketClientReady => _discordSocketClientReadyCompletionSource.Task;

    private Task OnDiscordSocketClientReady()
    {
        _discordSocketClientReadyCompletionSource.SetResult();
        return Task.CompletedTask;
    }

    private readonly TaskCompletionSource _discordSocketClientReadyCompletionSource = new();
}