using Configuration;
using Constants;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quartz;
using QuartzExtensions;
using UseCases.InputPorts.Club;

namespace Infrastructure.InputAdapters.Jobs;

[DisallowConcurrentExecution]
[ConfiguredCronJob(ConfigKeys.GeoGuessrClubSyncScheduleConfigurationKey)]
public class SyncClubJob(IOptions<GeoGuessrConfiguration> geoGuessrConfig, IServiceProvider serviceProvider, ILogger<SyncClubJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
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
                logger.LogError(ex, "Error syncing club {ClubId}.", club.ClubId);
            }
        }
    }
}
