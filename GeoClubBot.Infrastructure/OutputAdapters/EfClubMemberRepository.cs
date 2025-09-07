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
        var clubMemberExists = await dbContext.ClubMembers.AnyAsync(m => m.UserId == clubMember.UserId);

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

    public async Task<ClubMember> CreateOrUpdateClubMemberAsync(ClubMember clubMember)
    {
        // Try to find an existing club member with that id
        var clubMemberExists = await dbContext.ClubMembers.AnyAsync(m => m.UserId == clubMember.UserId);

        // If the club member already exists
        if (clubMemberExists)
        {
            // Update the club member
            dbContext.Update(clubMember);
        }
        else
        {
            // Add the club member
            dbContext.Add(clubMember);
        }

        // Save the changes to the database
        await dbContext.SaveChangesAsync();

        return clubMember;
    }

    public async Task<ClubMember?> ReadClubMemberByNicknameAsync(string nickname)
    {
        // Try to find the club member by nickname
        var clubMember = await dbContext.ClubMembers
            .Include(m => m.User)
            .FirstOrDefaultAsync(m => m.User!.Nickname == nickname);

        return clubMember;
    }

    public async Task<ClubMember?> ReadClubMemberByUserIdAsync(string userId)
    {
        // Try to find the club member
        var clubMember = await dbContext.ClubMembers
            .Include(m => m.User)
            .FirstOrDefaultAsync(m => m.UserId == userId);
        
        return clubMember;
    }

    public async Task<int> DeleteClubMembersWithoutHistoryAndStrikesAsync()
    {
        // Delete the entities
        var numDeletedClubMembers = await dbContext.ClubMembers
            .Include(m => m.History)
            .Include(m => m.Strikes)
            .Where(m => !m.History!.Any() && !m.Strikes!.Any())
            .ExecuteDeleteAsync();

        return numDeletedClubMembers;
    }
}