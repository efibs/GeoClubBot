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

        // Login the bot
        await client.LoginAsync(TokenType.Bot, token).ConfigureAwait(false);

        // Start the bot
        await client.StartAsync().ConfigureAwait(false);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        // Nothing to do here
        return Task.CompletedTask;
    }
}