using Constants;
using Microsoft.Extensions.Logging;
using Quartz;
using QuartzExtensions;
using UseCases.InputPorts.DailyMissionLogging;

namespace Infrastructure.InputAdapters.Jobs;

[DisallowConcurrentExecution]
[ConfiguredCronJob(ConfigKeys.DailyMissionLoggingCronScheduleConfigurationKey)]
public class DailyMissionLoggingJob(
    ILogDailyMissionsUseCase logDailyMissionsUseCase,
    ILogger<DailyMissionLoggingJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        try
        {
            await logDailyMissionsUseCase.LogDailyMissionsAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to log daily missions.");
        }
    }
}
