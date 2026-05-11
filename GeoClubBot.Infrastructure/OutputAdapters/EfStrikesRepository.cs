using Entities;
using Infrastructure.OutputAdapters.DataAccess;
using Microsoft.EntityFrameworkCore;
using UseCases.OutputPorts;

namespace Infrastructure.OutputAdapters;

public class EfStrikesRepository(GeoClubBotDbContext dbContext) : IStrikesRepository
{
    public ClubMemberStrike CreateStrike(ClubMemberStrike strike)
    {
        // Add the strike
        dbContext.Add(strike);

        return strike;
    }

    public async Task<int?> ReadNumberOfActiveStrikesByMemberUserIdAsync(string memberUserId)
    {
        // Get the number of strikes
        var numStrikes = await dbContext.ClubMemberStrikes
            .Include(s => s.ClubMember)
            .Where(s => s.ClubMember!.UserId == memberUserId)
            .Where(s => s.Revoked == false)
            .CountAsync()
            .ConfigureAwait(false);
        
        return numStrikes;
    }

    public async Task<List<ClubMemberStrike>?> ReadStrikesByMemberNicknameAsync(string memberNickname)
    {
        // Read the member
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
        // Read the strikes
        var strikes = await dbContext.ClubMemberStrikes
            .AsNoTracking()
            .Include(s => s.ClubMember)
            .ThenInclude(m => m!.User)
            .ToListAsync()
            .ConfigureAwait(false);
        
        return strikes;
    }

    public async Task<ClubMemberStrike?> RevokeStrikeByIdAsync(Guid strikeId)
    {
        var strike = await dbContext.ClubMemberStrikes
            .Include(s => s.ClubMember)
            .ThenInclude(m => m!.User)
            .FirstOrDefaultAsync(s => s.StrikeId == strikeId)
            .ConfigureAwait(false);

        if (strike == null)
        {
            return null;
        }

        strike.Revoked = true;

        return strike;
    }

    public async Task<ClubMemberStrike?> UnrevokeStrikeByIdAsync(Guid strikeId)
    {
        var strike = await dbContext.ClubMemberStrikes
            .Include(s => s.ClubMember)
            .ThenInclude(m => m!.User)
            .FirstOrDefaultAsync(s => s.StrikeId == strikeId)
            .ConfigureAwait(false);

        if (strike == null)
        {
            return null;
        }

        strike.Revoked = false;

        return strike;
    }

    public async Task<int> DeleteStrikesBeforeAsync(DateTimeOffset threshold)
    {
        // Delete the strikes
        var numDeleted = await dbContext.ClubMemberStrikes
            .Where(s => s.Timestamp < threshold)
            .ExecuteDeleteAsync()
            .ConfigureAwait(false);
        
        return numDeleted;
    }
}