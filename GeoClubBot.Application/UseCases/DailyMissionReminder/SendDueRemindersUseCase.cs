using Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.InputPorts.DailyMissionReminder;
using UseCases.OutputPorts;
using UseCases.OutputPorts.Discord;

namespace UseCases.UseCases.DailyMissionReminder;

public partial class SendDueRemindersUseCase(
    IUnitOfWork unitOfWork,
    IDiscordDirectMessageAccess directMessageAccess,
    IOptions<DailyMissionReminderConfiguration> config,
    ILogger<SendDueRemindersUseCase> logger) : ISendDueRemindersUseCase
{
    public async Task SendDueRemindersAsync()
    {
        var now = DateTime.UtcNow;
        var currentTime = new TimeOnly(now.Hour, now.Minute);
        var today = DateOnly.FromDateTime(now);

        var dueReminders = await unitOfWork.DailyMissionReminders
            .ReadDueRemindersAsync(currentTime, today)
            .ConfigureAwait(false);

        if (dueReminders.Count == 0)
        {
            return;
        }

        LogSendingReminders(dueReminders.Count);

        var defaultMessage = config.Value.DefaultMessage;

        foreach (var reminder in dueReminders)
        {
            var message = string.IsNullOrWhiteSpace(reminder.CustomMessage)
                ? defaultMessage
                : reminder.CustomMessage;

            var sent = await directMessageAccess
                .SendDirectMessageAsync(reminder.DiscordUserId, message)
                .ConfigureAwait(false);

            if (sent)
            {
                reminder.LastSentDateUtc = today;
                LogReminderSent(reminder.DiscordUserId);
            }
            else
            {
                LogReminderFailed(reminder.DiscordUserId);
            }
        }

        await unitOfWork.SaveChangesAsync().ConfigureAwait(false);
    }

    [LoggerMessage(LogLevel.Information, "Sending {Count} daily mission reminders.")]
    partial void LogSendingReminders(int count);

    [LoggerMessage(LogLevel.Debug, "Daily mission reminder sent to user {DiscordUserId}.")]
    partial void LogReminderSent(ulong discordUserId);

    [LoggerMessage(LogLevel.Warning, "Failed to send daily mission reminder to user {DiscordUserId}.")]
    partial void LogReminderFailed(ulong discordUserId);
}
