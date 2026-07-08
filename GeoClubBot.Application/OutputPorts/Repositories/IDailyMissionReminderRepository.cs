using Entities;

namespace UseCases.OutputPorts.Repositories;

public interface IDailyMissionReminderRepository
{
    void AddReminder(DailyMissionReminder reminder);

    /// <summary>Read all of a user's reminders (no tracking), ordered by time. For listing/status.</summary>
    Task<List<DailyMissionReminder>> ReadRemindersAsync(ulong discordUserId, CancellationToken cancellationToken = default);

    /// <summary>Read all of a user's reminders as tracked entities, ordered by time. For add/clear.</summary>
    Task<List<DailyMissionReminder>> ReadRemindersForUpdateAsync(ulong discordUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Read a single tracked reminder by its id, scoped to its owner so one user can't touch another's
    /// reminder. Returns null when it doesn't exist or isn't owned by <paramref name="discordUserId"/>.
    /// </summary>
    Task<DailyMissionReminder?> ReadReminderForUpdateAsync(Guid id, ulong discordUserId, CancellationToken cancellationToken = default);

    Task<List<DailyMissionReminder>> ReadDueRemindersForUpdateAsync(TimeOnly currentTimeUtc, DateOnly todayUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// Read all tracked reminders that should already have fired today but were not sent today —
    /// i.e. reminders missed while the bot was down. A reminder whose time is still ahead today is
    /// not missed (the regular schedule will fire it), and misses from previous days are moot
    /// because those days' missions have expired.
    /// </summary>
    Task<List<DailyMissionReminder>> ReadMissedRemindersForUpdateAsync(TimeOnly currentTimeUtc, DateOnly todayUtc, CancellationToken cancellationToken = default);

    void DeleteReminder(DailyMissionReminder reminder);
}
