using Constants;
using MediatR;
using Microsoft.Extensions.Logging;
using Quartz;
using QuartzExtensions;
using UseCases.UseCases.DailyActivity;

namespace Infrastructure.InputAdapters.Jobs;

[DisallowConcurrentExecution]
[ConfiguredCronJob(ConfigKeys.DailyActivitySnapshotCronScheduleConfigurationKey)]
public partial class DailyActivitySnapshotJob(
    ISender mediator,
    ILogger<DailyActivitySnapshotJob> logger) : IJob
{
    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken)
    {
        try
        {
            await mediator.Send(new SnapshotDailyActivityCommand(), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogFailed(logger, ex);
        }
    }

    [LoggerMessage(LogLevel.Error, "Failed to snapshot daily club activity.")]
    static partial void LogFailed(ILogger<DailyActivitySnapshotJob> logger, Exception ex);
}
