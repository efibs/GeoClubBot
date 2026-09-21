using Entities;
using Infrastructure.OutputAdapters.DataAccess;
using Microsoft.EntityFrameworkCore;
using UseCases.OutputPorts.Repositories;

namespace Infrastructure.OutputAdapters.Repositories;

public class EfClubMemberRepository(GeoClubBotDbContext dbContext) : IClubMemberRepository
{
    public void AddClubMember(ClubMember clubMember)
    {
        // Mark only the club member as added (not its navigation properties) to avoid
        // re-inserting an already-tracked User entity.
        dbContext.Entry(clubMember).State = EntityState.Added;
    }

    public async Task<ClubMember?> ReadClubMemberByNicknameAsync(string nickname, CancellationToken cancellationToken = default)
    {
        return await dbContext.ClubMembers
            .AsNoTracking()
            .Include(m => m.User)
            .SingleOrDefaultAsync(m => m.User.Nickname == nickname, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<ClubMember?> ReadClubMemberByUserIdAsync(string userId, CancellationToken cancellationToken = default)
    {
        return await dbContext.ClubMembers
            .AsNoTracking()
            .Include(m => m.User)
            .FirstOrDefaultAsync(m => m.UserId == userId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<Dictionary<string, ClubMember>> ReadClubMembersByUserIdsAsync(IReadOnlyCollection<string> userIds, CancellationToken cancellationToken = default)
    {
        if (userIds.Count == 0)
        {
            return new Dictionary<string, ClubMember>();
        }

        var members = await dbContext.ClubMembers
            .AsNoTracking()
            .Include(m => m.User)
            .Where(m => userIds.Contains(m.UserId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return members.ToDictionary(m => m.UserId);
    }

    public async Task<ClubMember?> ReadForUpdateByUserIdAsync(string userId, CancellationToken cancellationToken = default)
    {
        return await dbContext.ClubMembers
            .Include(m => m.User)
            .FirstOrDefaultAsync(m => m.UserId == userId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<List<ClubMember>> ReadClubMembersAsync(CancellationToken cancellationToken = default)
    {
        return await dbContext.ClubMembers
            .AsNoTracking()
            .Include(m => m.User)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<List<string>> ReadAllNicknamesAsync(CancellationToken cancellationToken = default)
    {
        // Projection-only read for autocomplete: never materializes full ClubMember graphs.
        return await dbContext.ClubMembers
            .AsNoTracking()
            .Select(m => m.User.Nickname)
            .Distinct()
            .OrderBy(n => n)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<List<ClubMember>> ReadClubMembersByClubIdAsync(Guid clubId, CancellationToken cancellationToken = default)
    {
        return await dbContext.ClubMembers
            .AsNoTracking()
            .Include(m => m.User)
            .Where(m => m.ClubId == clubId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<List<ClubMember>> ReadMembersWithExpiredArchivedPrivateChannelsAsync(DateTimeOffset threshold,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.ClubMembers
            .AsNoTracking()
            .Include(m => m.User)
            .Where(m => m.ClubId == null
                        && m.PrivateTextChannelArchivedAt != null
                        && m.PrivateTextChannelArchivedAt < threshold)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<int> DeleteClubMembersWithoutHistoryAndStrikesAsync(CancellationToken cancellationToken = default)
    {
        return await dbContext.ClubMembers
            .Include(m => m.History)
            .Include(m => m.Strikes)
            // A member still holding a private channel is kept: deleting the row would strand the
            // Discord channel with nothing left pointing at it. The archive sweep clears the id
            // first, and the next cleanup then removes the row.
            .Where(m => !m.History.Any() && !m.Strikes.Any() && m.PrivateTextChannelId == null)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
