using Entities;
using Infrastructure.OutputAdapters.DataAccess;
using Microsoft.EntityFrameworkCore;
using UseCases.OutputPorts.Repositories;

namespace Infrastructure.OutputAdapters.Repositories;

public class EfDailyMissionRepository(GeoClubBotDbContext dbContext) : IDailyMissionRepository
{
    public void AddRange(IEnumerable<DailyMission> missions)
    {
        dbContext.DailyMissions.AddRange(missions);
    }

    public async Task<IReadOnlyList<DailyMission>> ReadLatestFetchedMissionsAsync(CancellationToken cancellationToken)
    {
        // The table is append-only: every fetch inserts a batch sharing one FetchedAtUtc.
        // The newest batch is the current set of daily missions.
        var latestFetchedAt = await dbContext.DailyMissions
            .MaxAsync(m => (DateTimeOffset?)m.FetchedAtUtc, cancellationToken)
            .ConfigureAwait(false);

        if (latestFetchedAt is null)
        {
            return [];
        }

        return await dbContext.DailyMissions
            .AsNoTracking()
            .Where(m => m.FetchedAtUtc == latestFetchedAt.Value)
            .OrderBy(m => m.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
