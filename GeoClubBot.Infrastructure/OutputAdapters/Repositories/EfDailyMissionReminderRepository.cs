using Entities;
using Infrastructure.OutputAdapters.DataAccess;
using Microsoft.EntityFrameworkCore;
using UseCases.OutputPorts.Repositories;

namespace Infrastructure.OutputAdapters.Repositories;

public class EfDailyMissionReminderRepository(GeoClubBotDbContext dbContext) : IDailyMissionReminderRepository
{
    public void AddReminder(DailyMissionReminder reminder)
    {
        dbContext.Add(reminder);
    }

    public async Task<DailyMissionReminder?> ReadReminderAsync(ulong discordUserId, CancellationToken cancellationToken = default)
    {
        return await dbContext.DailyMissionReminders
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.DiscordUserId == discordUserId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<DailyMissionReminder?> ReadReminderForUpdateAsync(ulong discordUserId, CancellationToken cancellationToken = default)
    {
        return await dbContext.DailyMissionReminders
            .FindAsync([discordUserId], cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<List<DailyMissionReminder>> ReadDueRemindersForUpdateAsync(TimeOnly currentTimeUtc, DateOnly todayUtc, CancellationToken cancellationToken = default)
    {
        return await dbContext.DailyMissionReminders
            .Where(r => r.ReminderTimeUtc == currentTimeUtc
                        && (r.LastSentDateUtc == null || r.LastSentDateUtc < todayUtc))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public void DeleteReminder(DailyMissionReminder reminder)
    {
        dbContext.DailyMissionReminders.Remove(reminder);
    }
}
