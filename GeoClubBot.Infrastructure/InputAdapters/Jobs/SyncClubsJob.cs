using Constants;
using MediatR;
using Microsoft.Extensions.Logging;
using Quartz;
using QuartzExtensions;
using UseCases.UseCases.Club;

namespace Infrastructure.InputAdapters.Jobs;

[DisallowConcurrentExecution]
[ConfiguredCronJob(ConfigKeys.GeoGuessrClubSyncScheduleConfigurationKey)]
public partial class SyncClubsJob(ISender mediator, ILogger<SyncClubsJob> logger) : IJob
{
    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken)
    {
        try
        {
            await mediator.Send(new SyncClubsCommand(), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogFailed(logger, ex);
        }
    }

    [LoggerMessage(LogLevel.Error, "Error syncing clubs in periodical job.")]
    static partial void LogFailed(ILogger<SyncClubsJob> logger, Exception ex);
}
