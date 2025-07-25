using Entities;
using Infrastructure.OutputAdapters.DataAccess;
using Microsoft.EntityFrameworkCore;
using UseCases.OutputPorts;

namespace Infrastructure.OutputAdapters;

public class EfClubMemberRepository(GeoClubBotDbContext dbContext) : IClubMemberRepository
{
    public async Task<ClubMember?> CreateClubMemberAsync(ClubMember clubMember)
    {
        // Try to find an existing club member with that id
        var clubMemberExists = dbContext.ClubMembers.Any(m => m.UserId == clubMember.UserId);
        
        // If the club member already exists
        if (clubMemberExists)
        {
            return null;
        }
        
        // Add the club member
        dbContext.Add(clubMember);
        
        // Save the changes to the database
        await dbContext.SaveChangesAsync();
        
        return clubMember;
    }

    public async Task<ClubMember?> ReadClubMemberByNicknameAsync(string nickname)
    {
        // Try to find the club member by nickname
        var clubMember = await dbContext.ClubMembers
            .FirstOrDefaultAsync(m => m.Nickname == nickname);
        
        return clubMember;
    }

    public async Task<int> DeleteClubMembersWithoutHistoryAsync()
    {
        // Delete the entities
        var numDeletedClubMembers = await dbContext.ClubMembers
            .Where(m => m.History != null && !m.History.Any())
            .ExecuteDeleteAsync();
        
        return numDeletedClubMembers;
    }
}