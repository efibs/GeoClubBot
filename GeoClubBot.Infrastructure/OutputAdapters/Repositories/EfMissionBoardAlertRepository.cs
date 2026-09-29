using Entities;
using Infrastructure.OutputAdapters.DataAccess;
using Microsoft.EntityFrameworkCore;
using UseCases.OutputPorts.Repositories;

namespace Infrastructure.OutputAdapters.Repositories;

public class EfMissionBoardAlertRepository(GeoClubBotDbContext dbContext) : IMissionBoardAlertRepository
{
    public async Task<HashSet<(Guid MissionId, MissionBoardAlertKind Kind)>> ReadSentAlertsAsync(
        Guid clubId,
        IReadOnlyCollection<Guid> missionIds,
        CancellationToken cancellationToken = default)
    {
        var sent = await dbContext.MissionBoardAlerts
            .AsNoTracking()
            .Where(a => a.ClubId == clubId && missionIds.Contains(a.MissionId))
            .Select(a => new { a.MissionId, a.Kind })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return sent.Select(a => (a.MissionId, a.Kind)).ToHashSet();
    }

    public void Add(MissionBoardAlert alert)
    {
        dbContext.MissionBoardAlerts.Add(alert);
    }

    public async Task<int> DeleteSentBeforeAsync(DateTimeOffset threshold, CancellationToken cancellationToken = default)
    {
        return await dbContext.MissionBoardAlerts
            .Where(a => a.SentAt < threshold)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
