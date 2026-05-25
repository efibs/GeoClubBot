using Entities;
using Infrastructure.OutputAdapters.DataAccess;
using Microsoft.EntityFrameworkCore;
using UseCases.OutputPorts;

namespace Infrastructure.OutputAdapters;

public class EfStrikesRepository(GeoClubBotDbContext dbContext) : IStrikesRepository
{
    public ClubMemberStrike CreateStrike(ClubMemberStrike strike)
    {
        dbContext.Add(strike);
        return strike;
    }

    public async Task<int?> ReadNumberOfActiveStrikesByMemberUserIdAsync(string memberUserId)
    {
        return await dbContext.ClubMemberStrikes
            .AsNoTracking()
            .WhereActive()
            .Where(s => s.ClubMember!.UserId == memberUserId)
            .CountAsync()
            .ConfigureAwait(false);
    }

    public async Task<Dictionary<string, int>> ReadActiveStrikeCountsByMemberUserIdsAsync(IEnumerable<string> memberUserIds)
    {
        // Materialize the input set once so EF translates it as a single IN-list
        var userIdSet = memberUserIds.ToHashSet();

        return await dbContext.ClubMemberStrikes
            .AsNoTracking()
            .WhereActive()
            .Where(s => s.ClubMember!.UserId != null && userIdSet.Contains(s.ClubMember.UserId))
            .GroupBy(s => s.ClubMember!.UserId)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.UserId, x => x.Count)
            .ConfigureAwait(false);
    }

    public async Task<List<ClubMemberStrike>?> ReadStrikesByMemberNicknameAsync(string memberNickname)
    {
        var member = await dbContext.ClubMembers
            .AsNoTracking()
            .Include(m => m.Strikes)
            .Include(m => m.User)
            .FirstOrDefaultAsync(m => m.User!.Nickname == memberNickname)
            .ConfigureAwait(false);

        return member?.Strikes;
    }

    public async Task<List<ClubMemberStrike>> ReadAllStrikesAsync()
    {
        return await dbContext.ClubMemberStrikes
            .AsNoTracking()
            .Include(s => s.ClubMember)
            .ThenInclude(m => m!.User)
            .ToListAsync()
            .ConfigureAwait(false);
    }

    public async Task<ClubMemberStrike?> ReadForUpdateByIdAsync(Guid strikeId)
    {
        return await dbContext.ClubMemberStrikes
            .Include(s => s.ClubMember)
            .ThenInclude(m => m!.User)
            .FirstOrDefaultAsync(s => s.StrikeId == strikeId)
            .ConfigureAwait(false);
    }

    public async Task<int> DeleteStrikesBeforeAsync(DateTimeOffset threshold)
    {
        return await dbContext.ClubMemberStrikes
            .Where(s => s.Timestamp < threshold)
            .ExecuteDeleteAsync()
            .ConfigureAwait(false);
    }
}
