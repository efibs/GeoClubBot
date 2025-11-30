using Entities;
using Infrastructure.OutputAdapters.DataAccess;
using Microsoft.EntityFrameworkCore;
using UseCases.OutputPorts;

namespace Infrastructure.OutputAdapters;

public class EfClubMemberRepository(GeoClubBotDbContext dbContext) : IClubMemberRepository
{
    public ClubMember CreateClubMember(ClubMember clubMember)
    {
        // Add the club member
        dbContext.Add(clubMember);

        return clubMember;
    }
    
    public async Task<ClubMember?> UpdateClubMemberAsync(ClubMember clubMember)
    {
        // Get the database entry
        var dbEntry = await dbContext.ClubMembers
            .FindAsync(clubMember.UserId)
            .ConfigureAwait(false);
        
        // If the entity was not found
        if (dbEntry == null)
        {
            return null;
        }
        
        // Update the club member
        dbEntry.ClubId = clubMember.ClubId;
        dbEntry.IsCurrentlyMember = clubMember.IsCurrentlyMember;
        dbEntry.Xp = clubMember.Xp;
        dbEntry.JoinedAt = clubMember.JoinedAt;
        dbEntry.PrivateTextChannelId = clubMember.PrivateTextChannelId;

        return dbEntry;
    }

    public async Task<ClubMember?> ReadClubMemberByNicknameAsync(string nickname)
    {
        // Try to find the club member by nickname
        var clubMember = await dbContext.ClubMembers
            .Include(m => m.User)
            .AsNoTracking()
            .SingleOrDefaultAsync(m => m.User.Nickname == nickname)
            .ConfigureAwait(false);

        return clubMember;
    }

    public async Task<ClubMember?> ReadClubMemberByUserIdAsync(string userId)
    {
        // Try to find the club member
        var clubMember = await dbContext.ClubMembers
            .Include(m => m.User)
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.UserId == userId)
            .ConfigureAwait(false);
        
        return clubMember;
    }

    public async Task<List<ClubMember>> ReadClubMembersAsync()
    {
        // Get the club members
        var clubMembers = await dbContext.ClubMembers
            .AsNoTracking()
            .Include(m => m.User)
            .ToListAsync()
            .ConfigureAwait(false);
        
        return clubMembers;
    }

    public async Task<int> DeleteClubMembersWithoutHistoryAndStrikesAsync()
    {
        // Delete the entities
        var numDeletedClubMembers = await dbContext.ClubMembers
            .Include(m => m.History)
            .Include(m => m.Strikes)
            .Where(m => !m.History.Any() && !m.Strikes.Any())
            .ExecuteDeleteAsync()
            .ConfigureAwait(false);

        return numDeletedClubMembers;
    }
}