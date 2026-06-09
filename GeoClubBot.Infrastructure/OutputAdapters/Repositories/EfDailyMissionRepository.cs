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

    public async Task<IReadOnlyList<DailyMission>> ReadMissionsFetchedBetweenAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken)
    {
        return await dbContext.DailyMissions
            .AsNoTracking()
            .Where(m => m.FetchedAtUtc >= fromUtc && m.FetchedAtUtc < toUtc)
            .OrderBy(m => m.FetchedAtUtc)
            .ThenBy(m => m.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<DailyMissionKind>> ReadDistinctMissionKindsAsync(CancellationToken cancellationToken)
    {
        // Distinct over a constructor-projected record does not translate to SQL,
        // so project to an anonymous type first and map afterwards.
        var kinds = await dbContext.DailyMissions
            .AsNoTracking()
            .Select(m => new { m.Type, m.GameMode })
            .Distinct()
            .OrderBy(k => k.Type)
            .ThenBy(k => k.GameMode)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return kinds.Select(k => new DailyMissionKind(k.Type, k.GameMode)).ToList();
    }
}
