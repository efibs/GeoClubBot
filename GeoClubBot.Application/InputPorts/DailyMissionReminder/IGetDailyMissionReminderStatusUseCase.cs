namespace UseCases.InputPorts.DailyMissionReminder;

public interface IGetDailyMissionReminderStatusUseCase
{
    Task<Entities.DailyMissionReminder?> GetStatusAsync(ulong discordUserId);
}
