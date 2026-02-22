using Configuration;
using GeoClubBot.Discord.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.InputPorts.Club;

namespace Infrastructure.InputAdapters;

public class InitialSyncService(DiscordBotReadyService botReadyService, IServiceProvider serviceProvider, IOptions<GeoGuessrConfiguration> geoGuessrConfig, ILogger<InitialSyncService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Wait for the bot to be ready
        await botReadyService.DiscordSocketClientReady.ConfigureAwait(false);

        // Sync all clubs
        foreach (var club in geoGuessrConfig.Value.Clubs)
        {
            try
            {
                using var scope = serviceProvider.CreateScope();
                var useCase = scope.ServiceProvider.GetRequiredService<ISyncClubUseCase>();
                await useCase.SyncClubAsync(club.ClubId).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error during initial sync of club {ClubId}.", club.ClubId);
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        // Nothing to do
        return Task.CompletedTask;
    }
}
