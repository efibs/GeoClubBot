using Entities;
using Infrastructure.OutputAdapters.DataAccess;
using Microsoft.EntityFrameworkCore;
using UseCases.OutputPorts;

namespace Infrastructure.OutputAdapters;

public class EfClubMemberRepository(GeoClubBotDbContext dbContext) : IClubMemberRepository
{
    public async Task<ClubMember> CreateClubMemberAsync(ClubMember clubMember)
    {
        // Mark only the club member as added (not its navigation properties)
        // to avoid conflicts with an already-tracked User entity
        dbContext.Entry(clubMember).State = EntityState.Added;

        // Resolve the tracked User entity so the navigation property
        // includes DB-only properties like DiscordUserId
        var trackedUser = await dbContext.GeoGuessrUsers
            .FindAsync(clubMember.UserId)
            .ConfigureAwait(false);

        if (trackedUser != null)
        {
            clubMember.User = trackedUser;
        }

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

    public async Task<List<ClubMember>> ReadClubMembersByClubIdAsync(Guid clubId)
    {
        var clubMembers = await dbContext.ClubMembers
            .AsNoTracking()
            .Include(m => m.User)
            .Where(m => m.ClubId == clubId)
            .ToListAsync()
            .ConfigureAwait(false);

        return clubMembers;
    }

    public async Task<ClubMember?> SetPrivateTextChannelIdAsync(string userId, ulong privateTextChannelId)
    {
        var dbEntry = await dbContext.ClubMembers
            .FindAsync(userId)
            .ConfigureAwait(false);

        if (dbEntry is null)
        {
            return null;
        }

        dbEntry.PrivateTextChannelId = privateTextChannelId;

        return dbEntry;
    }

    public async Task<ClubMember?> ClearPrivateTextChannelIdAsync(string userId)
    {
        var dbEntry = await dbContext.ClubMembers
            .FindAsync(userId)
            .ConfigureAwait(false);

        if (dbEntry is null)
        {
            return null;
        }

        dbEntry.PrivateTextChannelId = null;

        return dbEntry;
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