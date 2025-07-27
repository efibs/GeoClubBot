using Entities;
using Infrastructure.OutputAdapters.DataAccess;
using Microsoft.EntityFrameworkCore;
using UseCases.OutputPorts;

namespace Infrastructure.OutputAdapters;

public class EfStrikesRepository(GeoClubBotDbContext dbContext) : IStrikesRepository
{
    public async Task<ClubMemberStrike?> CreateStrikeAsync(ClubMemberStrike strike)
    {
        // Try to find an existing strike with that id
        var strikeExists = await dbContext.ClubMemberStrikes.AnyAsync(s => s.StrikeId == strike.StrikeId);

        // If the strike already exists
        if (strikeExists)
        {
            return null;
        }

        // Add the strike
        dbContext.Add(strike);

        // Save the changes to the database
        await dbContext.SaveChangesAsync();

        return strike;
    }

    public async Task<int?> ReadNumberOfActiveStrikesByMemberUserIdAsync(string memberUserId)
    {
        // Get the number of strikes
        var numStrikes = await dbContext.ClubMemberStrikes
            .Include(s => s.ClubMember)
            .Where(s => s.ClubMember!.UserId == memberUserId)
            .Where(s => s.Revoked == false)
            .CountAsync();
        
        return numStrikes;
    }

    public async Task<List<ClubMemberStrike>?> ReadStrikesByMemberNicknameAsync(string memberNickname)
    {
        // Read the member
        var member = await dbContext.ClubMembers
            .Include(m => m.Strikes)
            .FirstOrDefaultAsync(m => m.Nickname == memberNickname);
        
        return member?.Strikes;
    }

    public async Task<bool> RevokeStrikeByIdAsync(Guid strikeId)
    {
        // Read the strike
        var strike = await dbContext.ClubMemberStrikes.FindAsync(strikeId);

        if (strike == null)
        {
            return false;
        }
        
        // Revoke the strike
        strike.Revoked = true;
        
        // Save the changes to the database
        await dbContext.SaveChangesAsync();

        return true;
    }
    
    public async Task<bool> UnrevokeStrikeByIdAsync(Guid strikeId)
    {
        // Read the strike
        var strike = await dbContext.ClubMemberStrikes.FindAsync(strikeId);

        if (strike == null)
        {
            return false;
        }
        
        // Unrevoke the strike
        strike.Revoked = false;
        
        // Save the changes to the database
        await dbContext.SaveChangesAsync();

        return true;
    }

    public async Task<int> DeleteStrikesBeforeAsync(DateTimeOffset threshold)
    {
        // Delete the strikes
        var numDeleted = await dbContext.ClubMemberStrikes
            .Where(s => s.Timestamp < threshold)
            .ExecuteDeleteAsync();
        
        return numDeleted;
    }
}