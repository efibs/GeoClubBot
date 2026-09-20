using Constants;
using MediatR;
using Microsoft.Extensions.Logging;
using Quartz;
using QuartzExtensions;
using UseCases.UseCases.DailyMissionStatistics;

namespace Infrastructure.InputAdapters.Jobs;

[DisallowConcurrentExecution]
[ConfiguredCronJob(ConfigKeys.DailyMissionStatisticsSnapshotCronScheduleConfigurationKey)]
public partial class DailyMissionCompletionSnapshotJob(
    ISender mediator,
    ILogger<DailyMissionCompletionSnapshotJob> logger) : IJob
{
    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken)
    {
        try
        {
            await mediator.Send(new SnapshotDailyMissionCompletionsCommand(), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogFailed(logger, ex);
        }
    }

    [LoggerMessage(LogLevel.Error, "Failed to snapshot daily mission completions.")]
    static partial void LogFailed(ILogger<DailyMissionCompletionSnapshotJob> logger, Exception ex);
}
