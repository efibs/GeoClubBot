using Configuration;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace GeoClubBot.Discord.Services;

/// <summary>
/// Class managing the discord socket
/// </summary>
/// <param name="client">The discord socket client</param>
public class DiscordBotService(DiscordSocketClient client, IOptions<DiscordConfiguration> config) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Login the bot
        await client.LoginAsync(TokenType.Bot, config.Value.BotToken).ConfigureAwait(false);

        // Start the bot
        await client.StartAsync().ConfigureAwait(false);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        // Shutdown the bot
        await client.LogoutAsync().ConfigureAwait(false);
        await client.StopAsync().ConfigureAwait(false);
    }
}