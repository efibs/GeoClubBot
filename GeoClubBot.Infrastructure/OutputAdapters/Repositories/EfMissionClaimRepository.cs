using Entities;
using Infrastructure.OutputAdapters.DataAccess;
using Microsoft.EntityFrameworkCore;
using UseCases.OutputPorts.Repositories;

namespace Infrastructure.OutputAdapters.Repositories;

public class EfMissionClaimRepository(GeoClubBotDbContext dbContext) : IMissionClaimRepository
{
    public async Task<List<MissionClaim>> ReadForUpdateByPeriodAsync(
        Guid clubId,
        DateTimeOffset periodStart,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.MissionClaims
            .Where(c => c.ClubId == clubId && c.PeriodStart == periodStart)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<List<MissionClaim>> ReadClaimsOfUserAsync(
        string userId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.MissionClaims
            .AsNoTracking()
            .Where(c => c.UserId == userId && c.ClaimedAt >= from && c.ClaimedAt <= to)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<List<MissionClaim>> ReadClaimsOfClubAsync(
        Guid clubId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.MissionClaims
            .AsNoTracking()
            .Where(c => c.ClubId == clubId && c.ClaimedAt >= from && c.ClaimedAt <= to)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public void Add(MissionClaim claim)
    {
        dbContext.MissionClaims.Add(claim);
    }

    public async Task<int> DeleteClaimedBeforeAsync(DateTimeOffset threshold, CancellationToken cancellationToken = default)
    {
        return await dbContext.MissionClaims
            .Where(c => c.ClaimedAt < threshold)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
