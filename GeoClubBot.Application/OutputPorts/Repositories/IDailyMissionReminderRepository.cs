using Entities;

namespace UseCases.OutputPorts.Repositories;

public interface IDailyMissionReminderRepository
{
    void AddReminder(DailyMissionReminder reminder);

    Task<DailyMissionReminder?> ReadReminderAsync(ulong discordUserId, CancellationToken cancellationToken = default);

    Task<DailyMissionReminder?> ReadReminderForUpdateAsync(ulong discordUserId, CancellationToken cancellationToken = default);

    Task<List<DailyMissionReminder>> ReadDueRemindersForUpdateAsync(TimeOnly currentTimeUtc, DateOnly todayUtc, CancellationToken cancellationToken = default);

    void DeleteReminder(DailyMissionReminder reminder);
}
