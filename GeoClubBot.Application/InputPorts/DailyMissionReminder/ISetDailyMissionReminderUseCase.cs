namespace UseCases.InputPorts.DailyMissionReminder;

public interface ISetDailyMissionReminderUseCase
{
    Task SetReminderAsync(ulong discordUserId, TimeOnly localTime, string? timeZoneId, string? customMessage);
}
