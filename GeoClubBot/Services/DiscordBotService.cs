using Constants;
using Discord;
using Discord.WebSocket;

namespace GeoClubBot.Services;

/// <summary>
/// Class managing the discord socket
/// </summary>
/// <param name="client">The discord socket client</param>
public class DiscordBotService(DiscordSocketClient client, IConfiguration config) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Get the token from the configuration
        var token = config.GetValue<string>(ConfigKeys.DiscordBotTokenConfigurationKey);

        // If the token was not given
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("Discord Bot token not set.");
        }

        // Attach the ready callback
        client.Ready += _onDiscordSocketClientReady;

        // Login the bot
        await client.LoginAsync(TokenType.Bot, token);

        // Start the bot
        await client.StartAsync();
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        // Nothing to do here
        return Task.CompletedTask;
    }

    public Task DiscordSocketClientReady => _discordSocketClientReadyCompletionSource.Task;

    private Task _onDiscordSocketClientReady()
    {
        _discordSocketClientReadyCompletionSource.SetResult();
        return Task.CompletedTask;
    }

    private readonly TaskCompletionSource _discordSocketClientReadyCompletionSource = new TaskCompletionSource();
}