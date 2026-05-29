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
    public async Task Execute(IJobExecutionContext context)
    {
        try
        {
            await mediator.Send(new CheckClubLevelCommand(), context.CancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogFailed(logger, ex);
        }
    }

    [LoggerMessage(LogLevel.Error, "Error checking club level.")]
    static partial void LogFailed(ILogger<CheckClubLevelJob> logger, Exception ex);
}
