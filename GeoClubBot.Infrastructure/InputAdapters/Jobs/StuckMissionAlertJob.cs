using Constants;
using MediatR;
using Microsoft.Extensions.Logging;
using Quartz;
using QuartzExtensions;
using UseCases.UseCases.MissionBoard;

namespace Infrastructure.InputAdapters.Jobs;

[DisallowConcurrentExecution]
[ConfiguredCronJob(ConfigKeys.MissionBoardAlertsCronScheduleConfigurationKey)]
public partial class StuckMissionAlertJob(ISender mediator, ILogger<StuckMissionAlertJob> logger) : IJob
{
    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken)
    {
        try
        {
            await mediator.Send(new CheckStuckMissionsCommand(), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogFailed(logger, ex);
        }
    }

    [LoggerMessage(LogLevel.Error, "Failed to check the mission boards for stuck missions.")]
    static partial void LogFailed(ILogger<StuckMissionAlertJob> logger, Exception ex);
}
