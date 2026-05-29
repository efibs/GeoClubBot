using Constants;
using MediatR;
using Microsoft.Extensions.Logging;
using Quartz;
using QuartzExtensions;
using UseCases.UseCases.DailyChallenge;

namespace Infrastructure.InputAdapters.Jobs;

[DisallowConcurrentExecution]
[ConfiguredCronJob(ConfigKeys.DailyChallengesCronScheduleConfigurationKey)]
public partial class DailyChallengeJob(ISender mediator, ILogger<DailyChallengeJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        try
        {
            await mediator.Send(new DailyChallengeCommand(), context.CancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogFailed(logger, ex);
        }
    }

    [LoggerMessage(LogLevel.Error, "Failed to create daily challenge.")]
    static partial void LogFailed(ILogger<DailyChallengeJob> logger, Exception ex);
}
