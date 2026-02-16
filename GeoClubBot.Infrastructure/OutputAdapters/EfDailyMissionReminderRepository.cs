using Entities;
using Infrastructure.OutputAdapters.DataAccess;
using Microsoft.EntityFrameworkCore;
using UseCases.OutputPorts;

namespace Infrastructure.OutputAdapters;

public class EfDailyMissionReminderRepository(GeoClubBotDbContext dbContext) : IDailyMissionReminderRepository
{
    public DailyMissionReminder CreateReminder(DailyMissionReminder reminder)
    {
        dbContext.Add(reminder);
        return reminder;
    }

    public async Task<DailyMissionReminder?> ReadReminderAsync(ulong discordUserId)
    {
        return await dbContext.DailyMissionReminders
            .FindAsync(discordUserId)
            .ConfigureAwait(false);
    }

    public async Task<List<DailyMissionReminder>> ReadDueRemindersAsync(TimeOnly currentTimeUtc, DateOnly todayUtc)
    {
        return await dbContext.DailyMissionReminders
            .Where(r => r.ReminderTimeUtc == currentTimeUtc
                        && (r.LastSentDateUtc == null || r.LastSentDateUtc < todayUtc))
            .ToListAsync()
            .ConfigureAwait(false);
    }

    public void DeleteReminder(DailyMissionReminder reminder)
    {
        dbContext.DailyMissionReminders.Remove(reminder);
    }
}
