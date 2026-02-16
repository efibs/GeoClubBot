using Entities;

namespace UseCases.OutputPorts;

public interface IDailyMissionReminderRepository
{
    DailyMissionReminder CreateReminder(DailyMissionReminder reminder);

    Task<DailyMissionReminder?> ReadReminderAsync(ulong discordUserId);

    Task<List<DailyMissionReminder>> ReadDueRemindersAsync(TimeOnly currentTimeUtc, DateOnly todayUtc);

    void DeleteReminder(DailyMissionReminder reminder);
}
