using GeoClubBot.Discord.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using UseCases.InputPorts.Club;

namespace Infrastructure.InputAdapters;

public class InitialSyncService(DiscordBotReadyService botReadyService, IServiceScopeFactory scopeFactory, ILogger<InitialSyncService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Wait for the bot to be ready
        await botReadyService.DiscordSocketClientReady.ConfigureAwait(false);

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var useCase = scope.ServiceProvider.GetRequiredService<ISyncClubsUseCase>();
            await useCase.SyncClubsAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during initial sync of clubs.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        // Nothing to do
        return Task.CompletedTask;
    }
}
