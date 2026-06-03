using Configuration;
using Discord.WebSocket;
using GeoClubBot.Discord.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace GeoClubBot.Discord.Logging;

/// <summary>
/// Drains <see cref="DiscordChannelLogQueue"/> and delivers each entry to the configured channel as
/// an embed. Waits for the gateway to be ready first, so logs emitted during start-up are buffered
/// and flushed once the client connects. Delivery failures are written to <see cref="Console.Error"/>
/// only — never back through <c>ILogger</c>, which would re-enter this sink.
/// </summary>
public sealed class DiscordChannelLogProcessor(
    DiscordChannelLogQueue queue,
    DiscordSocketClient client,
    DiscordBotReadyService readyService,
    DiscordLogEmbedFormatter formatter,
    IOptions<DiscordConfiguration> discordConfig,
    IOptions<DiscordLoggingConfiguration> loggingConfig) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = loggingConfig.Value;
        if (!settings.Enabled)
        {
            return;
        }

        // Buffer entries until the gateway is ready; resolving/ sending before then would fail.
        await readyService.DiscordSocketClientReady.WaitAsync(stoppingToken).ConfigureAwait(false);

        var serverId = discordConfig.Value.ServerId;
        var channelId = settings.ChannelId;

        try
        {
            await foreach (var entry in queue.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
            {
                await DeliverAsync(serverId, channelId, entry).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down — nothing to do.
        }
    }

    private async Task DeliverAsync(ulong serverId, ulong channelId, DiscordLogEntry entry)
    {
        try
        {
            var channel = client.GetGuild(serverId)?.GetTextChannel(channelId);
            if (channel is null)
            {
                await Console.Error.WriteLineAsync(
                    $"[DiscordChannelLogProcessor] Channel {channelId} not found; dropping log entry.")
                    .ConfigureAwait(false);
                return;
            }

            await channel.SendMessageAsync(embed: formatter.Build(entry)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Swallow and report to the console only; routing this through ILogger would loop.
            await Console.Error.WriteLineAsync(
                $"[DiscordChannelLogProcessor] Failed to deliver log entry: {ex.GetType().Name}: {ex.Message}")
                .ConfigureAwait(false);
        }
    }
}
