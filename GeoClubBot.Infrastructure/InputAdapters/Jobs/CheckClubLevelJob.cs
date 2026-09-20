using Constants;
using MediatR;
using Microsoft.Extensions.Logging;
using Quartz;
using QuartzExtensions;
using UseCases.UseCases.Club;

namespace Infrastructure.InputAdapters.Jobs;

[DisallowConcurrentExecution]
[ConfiguredCronJob(ConfigKeys.ClubLevelCheckerCronScheduleConfigurationKey)]
public partial class CheckClubLevelJob(ISender mediator, ILogger<CheckClubLevelJob> logger) : IJob
{
    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken)
    {
        try
        {
            await mediator.Send(new CheckClubLevelCommand(), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogFailed(logger, ex);
        }
    }

    [LoggerMessage(LogLevel.Error, "Error checking club level.")]
    static partial void LogFailed(ILogger<CheckClubLevelJob> logger, Exception ex);
}
