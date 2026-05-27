using Entities;

namespace UseCases.OutputPorts;

public interface IDailyMissionReminderRepository
{
    void AddReminder(DailyMissionReminder reminder);

    Task<DailyMissionReminder?> ReadReminderAsync(ulong discordUserId, CancellationToken cancellationToken = default);

    Task<DailyMissionReminder?> ReadReminderForUpdateAsync(ulong discordUserId, CancellationToken cancellationToken = default);

    Task<List<DailyMissionReminder>> ReadDueRemindersAsync(TimeOnly currentTimeUtc, DateOnly todayUtc, CancellationToken cancellationToken = default);

    void DeleteReminder(DailyMissionReminder reminder);
}
