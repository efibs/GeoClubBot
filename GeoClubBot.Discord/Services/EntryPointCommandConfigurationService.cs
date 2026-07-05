using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Configuration;
using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GeoClubBot.Discord.Services;

/// <summary>
/// Discord auto-creates a "Launch" Entry Point Command (type <c>PRIMARY_ENTRY_POINT</c>) whenever
/// Activities is enabled for the app, defaulting its handler to <c>DISCORD_LAUNCH_ACTIVITY</c> —
/// which posts a channel message every time anyone clicks Launch/Play. Discord.Net predates entry
/// point commands entirely (its <c>ApplicationCommandType</c> enum has no matching value), so this
/// talks to Discord's REST API directly, once at startup, to flip the handler to <c>APP_HANDLER</c>
/// (no auto-message). <see cref="EntryPointCommandGatewayListener"/> then answers the resulting
/// interactions itself with a silent <c>LAUNCH_ACTIVITY</c> response.
/// </summary>
public sealed class EntryPointCommandConfigurationService(
    HttpClient httpClient,
    DiscordSocketClient client,
    DiscordBotReadyService botReadyService,
    IOptions<DiscordConfiguration> config,
    ILogger<EntryPointCommandConfigurationService> logger) : IHostedService
{
    private const int EntryPointCommandType = 4;
    private const int AppHandler = 1;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await botReadyService.DiscordSocketClientReady.ConfigureAwait(false);

        try
        {
            var appInfo = await client.GetApplicationInfoAsync().ConfigureAwait(false);

            var commands = await GetApplicationCommandsAsync(appInfo.Id, cancellationToken).ConfigureAwait(false);
            var entryPoint = commands?.FirstOrDefault(c => c.Type == EntryPointCommandType);

            if (entryPoint is null)
            {
                logger.LogDebug("No entry point (Launch) command found; Activities may not be enabled for this application.");
                return;
            }

            if (entryPoint.Handler == AppHandler)
            {
                return;
            }

            await SetHandlerToAppHandlerAsync(appInfo.Id, entryPoint.Id, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to configure the entry point command handler.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task<List<ApplicationCommandDto>?> GetApplicationCommandsAsync(ulong applicationId, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"applications/{applicationId}/commands");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bot", config.Value.BotToken);

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "Failed to list application commands ({StatusCode}); leaving the entry point command handler unchanged.",
                response.StatusCode);
            return null;
        }

        return await response.Content.ReadFromJsonAsync<List<ApplicationCommandDto>>(cancellationToken).ConfigureAwait(false);
    }

    private async Task SetHandlerToAppHandlerAsync(ulong applicationId, string commandId, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch, $"applications/{applicationId}/commands/{commandId}")
        {
            Content = JsonContent.Create(new { handler = AppHandler })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bot", config.Value.BotToken);

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Failed to set the entry point command handler to APP_HANDLER ({StatusCode}).", response.StatusCode);
            return;
        }

        logger.LogInformation("Entry point command handler set to APP_HANDLER; Launch no longer posts a channel message.");
    }

    private sealed record ApplicationCommandDto(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("type")] int Type,
        [property: JsonPropertyName("handler")] int? Handler);
}
