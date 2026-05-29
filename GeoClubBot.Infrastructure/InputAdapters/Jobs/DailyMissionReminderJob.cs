using Constants;
using MediatR;
using Microsoft.Extensions.Logging;
using Quartz;
using QuartzExtensions;
using UseCases.UseCases.DailyMissionReminder;

namespace Infrastructure.InputAdapters.Jobs;

[DisallowConcurrentExecution]
[ConfiguredCronJob(ConfigKeys.DailyMissionReminderCronScheduleConfigurationKey)]
public partial class DailyMissionReminderJob(ISender mediator, ILogger<DailyMissionReminderJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        try
        {
            await mediator.Send(new SendDueRemindersCommand(), context.CancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogFailed(logger, ex);
        }
    }

    [LoggerMessage(LogLevel.Error, "Failed to send daily mission reminders.")]
    static partial void LogFailed(ILogger<DailyMissionReminderJob> logger, Exception ex);
}
