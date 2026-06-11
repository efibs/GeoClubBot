using Entities;
using Infrastructure.OutputAdapters.DataAccess;
using Microsoft.EntityFrameworkCore;
using UseCases.OutputPorts.Repositories;

namespace Infrastructure.OutputAdapters.Repositories;

public class EfDailyMissionCompletionRepository(GeoClubBotDbContext dbContext) : IDailyMissionCompletionRepository
{
    public void AddRange(IEnumerable<DailyMissionMemberCompletion> completions)
    {
        dbContext.DailyMissionMemberCompletions.AddRange(completions);
    }

    public async Task<bool> HasSnapshotForDayAsync(Guid clubId, DateOnly day, CancellationToken cancellationToken)
    {
        return await dbContext.DailyMissionMemberCompletions
            .AnyAsync(c => c.ClubId == clubId && c.Date == day, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<DailyMissionMemberCompletion>> ReadCompletionsAsync(
        Guid? clubId,
        DateOnly fromDay,
        DateOnly toDay,
        CancellationToken cancellationToken)
    {
        var query = dbContext.DailyMissionMemberCompletions
            .AsNoTracking()
            .Where(c => c.Date >= fromDay && c.Date <= toDay);

        if (clubId is not null)
        {
            query = query.Where(c => c.ClubId == clubId.Value);
        }

        return await query
            .OrderBy(c => c.Date)
            .ThenBy(c => c.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
