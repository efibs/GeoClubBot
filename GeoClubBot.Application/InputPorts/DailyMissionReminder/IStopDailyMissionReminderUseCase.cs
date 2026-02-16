namespace UseCases.InputPorts.DailyMissionReminder;

public interface IStopDailyMissionReminderUseCase
{
    Task<bool> StopReminderAsync(ulong discordUserId);
}
