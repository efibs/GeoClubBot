using Constants;
using MediatR;
using Microsoft.Extensions.Logging;
using Quartz;
using QuartzExtensions;
using UseCases.UseCases.DailyMissionLogging;

namespace Infrastructure.InputAdapters.Jobs;

[DisallowConcurrentExecution]
[ConfiguredCronJob(ConfigKeys.DailyMissionLoggingCronScheduleConfigurationKey)]
public partial class DailyMissionLoggingJob(ISender mediator, ILogger<DailyMissionLoggingJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        try
        {
            await mediator.Send(new LogDailyMissionsCommand(), context.CancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogFailed(logger, ex);
        }
    }

    [LoggerMessage(LogLevel.Error, "Failed to log daily missions.")]
    static partial void LogFailed(ILogger<DailyMissionLoggingJob> logger, Exception ex);
}
