using Constants;
using MediatR;
using Microsoft.Extensions.Logging;
using Quartz;
using QuartzExtensions;
using UseCases.UseCases.MissionBoardNotifications;

namespace Infrastructure.InputAdapters.Jobs;

/// <summary>
/// The safety net of the board change DMs (#504): a change pushed over GeoGuessr's socket is
/// handled at once, but a missed push (or a week start that pushes nothing) is caught here.
/// </summary>
[DisallowConcurrentExecution]
[ConfiguredCronJob(ConfigKeys.MissionBoardNotificationsCronScheduleConfigurationKey)]
public partial class MissionBoardChangeJob(ISender mediator, ILogger<MissionBoardChangeJob> logger) : IJob
{
    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken)
    {
        try
        {
            await mediator.Send(new NotifyMissionBoardChangesCommand(), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogFailed(logger, ex);
        }
    }

    [LoggerMessage(LogLevel.Error, "Failed to check the mission boards for changes.")]
    static partial void LogFailed(ILogger<MissionBoardChangeJob> logger, Exception ex);
}
