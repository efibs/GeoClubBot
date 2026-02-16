using Microsoft.Extensions.Logging;
using UseCases.InputPorts.DailyMissionReminder;
using UseCases.OutputPorts;

namespace UseCases.UseCases.DailyMissionReminder;

public partial class SetDailyMissionReminderUseCase(IUnitOfWork unitOfWork, ILogger<SetDailyMissionReminderUseCase> logger)
    : ISetDailyMissionReminderUseCase
{
    public async Task SetReminderAsync(ulong discordUserId, TimeOnly localTime, string? timeZoneId, string? customMessage)
    {
        // Convert local time to UTC
        var utcTime = _convertToUtc(localTime, timeZoneId);

        // Check if a reminder already exists
        var existing = await unitOfWork.DailyMissionReminders.ReadReminderAsync(discordUserId).ConfigureAwait(false);

        if (existing != null)
        {
            // Update existing reminder
            existing.ReminderTimeUtc = utcTime;
            existing.TimeZoneId = timeZoneId;
            existing.CustomMessage = customMessage;
            existing.LastSentDateUtc = null;

            LogReminderUpdated(discordUserId, utcTime);
        }
        else
        {
            // Create new reminder
            var reminder = new Entities.DailyMissionReminder
            {
                DiscordUserId = discordUserId,
                ReminderTimeUtc = utcTime,
                TimeZoneId = timeZoneId,
                CustomMessage = customMessage
            };

            unitOfWork.DailyMissionReminders.CreateReminder(reminder);

            LogReminderCreated(discordUserId, utcTime);
        }

        await unitOfWork.SaveChangesAsync().ConfigureAwait(false);
    }

    private static TimeOnly _convertToUtc(TimeOnly localTime, string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            return localTime;
        }

        var tz = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var localDateTime = today.ToDateTime(localTime);
        var utcDateTime = TimeZoneInfo.ConvertTimeToUtc(localDateTime, tz);

        return TimeOnly.FromDateTime(utcDateTime);
    }

    [LoggerMessage(LogLevel.Information, "Daily mission reminder updated for user {DiscordUserId} at {UtcTime} UTC.")]
    partial void LogReminderUpdated(ulong discordUserId, TimeOnly utcTime);

    [LoggerMessage(LogLevel.Information, "Daily mission reminder created for user {DiscordUserId} at {UtcTime} UTC.")]
    partial void LogReminderCreated(ulong discordUserId, TimeOnly utcTime);
}
