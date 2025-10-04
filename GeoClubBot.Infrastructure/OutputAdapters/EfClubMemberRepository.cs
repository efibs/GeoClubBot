using Entities;
using Infrastructure.OutputAdapters.DataAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UseCases.OutputPorts;

namespace Infrastructure.OutputAdapters;

public class EfClubMemberRepository(GeoClubBotDbContext dbContext, ILogger<EfClubMemberRepository> logger) : IClubMemberRepository
{
    public async Task<ClubMember> CreateClubMemberAsync(ClubMember clubMember)
    {
        // Deep copy the club member
        var clubMemberCopy = clubMember.ShallowCopy();
        
        // Null out navigation properties
        clubMemberCopy.User = null;
        clubMemberCopy.Club = null;
        clubMemberCopy.Excuses = null;
        clubMemberCopy.History = null;
        clubMemberCopy.Strikes = null;
        
        // Add the club member
        dbContext.Add(clubMemberCopy);
        
        // Save the changes to the database
        await dbContext.SaveChangesAsync().ConfigureAwait(false);
        
        // Reload the navigation properties
        await dbContext.Entry(clubMemberCopy).Reference(m => m.User).LoadAsync().ConfigureAwait(false);
        await dbContext.Entry(clubMemberCopy).Reference(m => m.Club).LoadAsync().ConfigureAwait(false);
        await dbContext.Entry(clubMemberCopy).Collection(m => m.Excuses).LoadAsync().ConfigureAwait(false);
        await dbContext.Entry(clubMemberCopy).Collection(m => m.History).LoadAsync().ConfigureAwait(false);
        await dbContext.Entry(clubMemberCopy).Collection(m => m.Strikes).LoadAsync().ConfigureAwait(false);
        
        return clubMemberCopy;
    }
    
    public async Task<ClubMember?> UpdateClubMemberAsync(ClubMember clubMember)
    {
        // Deep copy the club member
        var clubMemberCopy = clubMember.ShallowCopy();

        // Null out navigation properties
        clubMemberCopy.User = null;
        clubMemberCopy.Club = null;
        clubMemberCopy.Excuses = null;
        clubMemberCopy.History = null;
        clubMemberCopy.Strikes = null;
        
        // Get the database entry
        var dbEntry = await dbContext.ClubMembers
            .FindAsync(clubMemberCopy.UserId)
            .ConfigureAwait(false);
        
        // If the entity was not found
        if (dbEntry == null)
        {
            return null;
        }
        
        // Update the club member
        dbContext.Entry(dbEntry).CurrentValues.SetValues(clubMemberCopy);

        // Save the changes to the database
        await dbContext.SaveChangesAsync().ConfigureAwait(false);

        // Reload the navigation properties
        await dbContext.Entry(dbEntry).Reference(m => m.User).LoadAsync().ConfigureAwait(false);
        await dbContext.Entry(dbEntry).Reference(m => m.Club).LoadAsync().ConfigureAwait(false);
        await dbContext.Entry(dbEntry).Collection(m => m.Excuses).LoadAsync().ConfigureAwait(false);
        await dbContext.Entry(dbEntry).Collection(m => m.History).LoadAsync().ConfigureAwait(false);
        await dbContext.Entry(dbEntry).Collection(m => m.Strikes).LoadAsync().ConfigureAwait(false);
        
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
            .SingleOrDefaultAsync(m => m.UserId == userId)
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
            .Where(m => !m.History!.Any() && !m.Strikes!.Any())
            .ExecuteDeleteAsync().ConfigureAwait(false);

        return numDeletedClubMembers;
    }
}