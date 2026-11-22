using Entities;
using Infrastructure.OutputAdapters.DataAccess;
using Microsoft.EntityFrameworkCore;
using UseCases.OutputPorts.Repositories;

namespace Infrastructure.OutputAdapters.Repositories;

public class EfStrikesRepository(GeoClubBotDbContext dbContext) : IStrikesRepository
{
    public ClubMemberStrike CreateStrike(ClubMemberStrike strike)
    {
        dbContext.Add(strike);
        return strike;
    }

    public async Task<int?> ReadNumberOfActiveStrikesByMemberUserIdAsync(string memberUserId, CancellationToken cancellationToken = default)
    {
        return await dbContext.ClubMemberStrikes
            .AsNoTracking()
            .WhereActive()
            .Where(s => s.ClubMember!.UserId == memberUserId)
            .CountAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<Dictionary<string, int>> ReadActiveStrikeCountsByMemberUserIdsAsync(IEnumerable<string> memberUserIds, CancellationToken cancellationToken = default)
    {
        // Materialize the input set once so EF translates it as a single IN-list
        var userIdSet = memberUserIds.ToHashSet();

        return await dbContext.ClubMemberStrikes
            .AsNoTracking()
            .WhereActive()
            .Where(s => s.ClubMember!.UserId != null && userIdSet.Contains(s.ClubMember.UserId))
            .GroupBy(s => s.ClubMember!.UserId)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.UserId, x => x.Count, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<List<ClubMemberStrike>> ReadStrikesByMemberUserIdAsync(string memberUserId, CancellationToken cancellationToken = default)
    {
        return await dbContext.ClubMemberStrikes
            .AsNoTracking()
            .Where(s => s.UserId == memberUserId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<List<ClubMemberStrike>> ReadAllStrikesAsync(CancellationToken cancellationToken = default)
    {
        return await dbContext.ClubMemberStrikes
            .AsNoTracking()
            .Include(s => s.ClubMember)
            .ThenInclude(m => m!.User)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<ClubMemberStrike?> ReadForUpdateByIdAsync(Guid strikeId, CancellationToken cancellationToken = default)
    {
        return await dbContext.ClubMemberStrikes
            .Include(s => s.ClubMember)
            .ThenInclude(m => m!.User)
            .FirstOrDefaultAsync(s => s.StrikeId == strikeId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<List<ClubMemberStrike>> ReadAllActiveForUpdateAsync(CancellationToken cancellationToken = default)
    {
        return await dbContext.ClubMemberStrikes
            .WhereActive()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<int> DeleteStrikesBeforeAsync(DateTimeOffset threshold, CancellationToken cancellationToken = default)
    {
        return await dbContext.ClubMemberStrikes
            .Where(s => s.Timestamp < threshold)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
