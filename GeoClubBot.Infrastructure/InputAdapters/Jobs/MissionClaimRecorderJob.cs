using Constants;
using MediatR;
using Microsoft.Extensions.Logging;
using Quartz;
using QuartzExtensions;
using UseCases.UseCases.MissionBoard;

namespace Infrastructure.InputAdapters.Jobs;

[DisallowConcurrentExecution]
[ConfiguredCronJob(ConfigKeys.MissionClaimTrackingCronScheduleConfigurationKey)]
public partial class MissionClaimRecorderJob(ISender mediator, ILogger<MissionClaimRecorderJob> logger) : IJob
{
    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken)
    {
        try
        {
            await mediator.Send(new RecordMissionClaimsCommand(), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogFailed(logger, ex);
        }
    }

    [LoggerMessage(LogLevel.Error, "Failed to record the mission claims.")]
    static partial void LogFailed(ILogger<MissionClaimRecorderJob> logger, Exception ex);
}
