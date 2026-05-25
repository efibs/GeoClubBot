using Entities;
using Infrastructure.OutputAdapters.DataAccess;
using Microsoft.EntityFrameworkCore;
using UseCases.OutputPorts;

namespace Infrastructure.OutputAdapters;

public class EfClubMemberRepository(GeoClubBotDbContext dbContext) : IClubMemberRepository
{
    public void AddClubMember(ClubMember clubMember)
    {
        // Mark only the club member as added (not its navigation properties) to avoid
        // re-inserting an already-tracked User entity.
        dbContext.Entry(clubMember).State = EntityState.Added;
    }

    public async Task<ClubMember?> ReadClubMemberByNicknameAsync(string nickname)
    {
        return await dbContext.ClubMembers
            .AsNoTracking()
            .Include(m => m.User)
            .SingleOrDefaultAsync(m => m.User.Nickname == nickname)
            .ConfigureAwait(false);
    }

    public async Task<ClubMember?> ReadClubMemberByUserIdAsync(string userId)
    {
        return await dbContext.ClubMembers
            .AsNoTracking()
            .Include(m => m.User)
            .FirstOrDefaultAsync(m => m.UserId == userId)
            .ConfigureAwait(false);
    }

    public async Task<ClubMember?> ReadForUpdateByUserIdAsync(string userId)
    {
        return await dbContext.ClubMembers
            .Include(m => m.User)
            .FirstOrDefaultAsync(m => m.UserId == userId)
            .ConfigureAwait(false);
    }

    public async Task<List<ClubMember>> ReadClubMembersAsync()
    {
        return await dbContext.ClubMembers
            .AsNoTracking()
            .Include(m => m.User)
            .ToListAsync()
            .ConfigureAwait(false);
    }

    public async Task<List<ClubMember>> ReadClubMembersByClubIdAsync(Guid clubId)
    {
        return await dbContext.ClubMembers
            .AsNoTracking()
            .Include(m => m.User)
            .Where(m => m.ClubId == clubId)
            .ToListAsync()
            .ConfigureAwait(false);
    }

    public async Task<int> DeleteClubMembersWithoutHistoryAndStrikesAsync()
    {
        return await dbContext.ClubMembers
            .Include(m => m.History)
            .Include(m => m.Strikes)
            .Where(m => !m.History.Any() && !m.Strikes.Any())
            .ExecuteDeleteAsync()
            .ConfigureAwait(false);
    }
}
