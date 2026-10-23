using Constants;
using MediatR;
using Microsoft.Extensions.Logging;
using Quartz;
using QuartzExtensions;
using UseCases.UseCases.ClubApplications;

namespace Infrastructure.InputAdapters.Jobs;

[DisallowConcurrentExecution]
[ConfiguredCronJob(ConfigKeys.ClubApplicationNotificationsCronScheduleConfigurationKey)]
public partial class ClubApplicationCheckJob(ISender mediator, ILogger<ClubApplicationCheckJob> logger) : IJob
{
    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken)
    {
        try
        {
            await mediator.Send(new CheckClubApplicationsCommand(), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogFailed(logger, ex);
        }
    }

    [LoggerMessage(LogLevel.Error, "Failed to check the clubs for new applications.")]
    static partial void LogFailed(ILogger<ClubApplicationCheckJob> logger, Exception ex);
}
