using Constants;
using Microsoft.Extensions.Logging;
using Quartz;
using QuartzExtensions;
using UseCases.InputPorts.Club;

namespace Infrastructure.InputAdapters.Jobs;

[DisallowConcurrentExecution]
[ConfiguredCronJob(ConfigKeys.GeoGuessrClubSyncScheduleConfigurationKey)]
public class SyncClubsJob(ISyncClubsUseCase useCase, ILogger<SyncClubsJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        try
        {
            await useCase.SyncClubsAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error syncing clubs in periodical job.");
        }
    }
}
