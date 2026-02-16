using Constants;
using Microsoft.Extensions.Logging;
using Quartz;
using QuartzExtensions;
using UseCases.InputPorts.DailyMissionReminder;

namespace Infrastructure.InputAdapters.Jobs;

[DisallowConcurrentExecution]
[ConfiguredCronJob(ConfigKeys.DailyMissionReminderCronScheduleConfigurationKey)]
public class DailyMissionReminderJob(ISendDueRemindersUseCase sendDueRemindersUseCase, ILogger<DailyMissionReminderJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        try
        {
            await sendDueRemindersUseCase.SendDueRemindersAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send daily mission reminders.");
        }
    }
}
