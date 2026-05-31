using Entities;
using Infrastructure.OutputAdapters.DataAccess;
using Microsoft.EntityFrameworkCore;
using UseCases.OutputPorts.Projections;
using UseCases.OutputPorts.Repositories;

namespace Infrastructure.OutputAdapters.Repositories;

public class EfHistoryRepository(GeoClubBotDbContext dbContext) : IHistoryRepository
{
    public List<ClubMemberHistoryEntry> CreateHistoryEntries(ICollection<ClubMemberHistoryEntry> entries)
    {
        dbContext.AddRange(entries);
        return entries.ToList();
    }

    public async Task<List<ClubMemberHistoryEntry>> ReadHistoryEntriesAsync(Guid clubId, CancellationToken cancellationToken = default)
    {
        return await dbContext.ClubMemberHistoryEntries
            .Where(e => e.ClubId == clubId)
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<List<ClubMemberHistoryEntry>?> ReadHistoryEntriesByPlayerNicknameAsync(string playerNickname, Guid clubId, CancellationToken cancellationToken = default)
    {
        var playerExists = await dbContext.ClubMembers
            .Include(m => m.User)
            .AnyAsync(m => m.User!.Nickname == playerNickname, cancellationToken)
            .ConfigureAwait(false);

        if (!playerExists)
        {
            return null;
        }

        return await dbContext.ClubMemberHistoryEntries
            .AsNoTracking()
            .Include(e => e.ClubMember)
            .ThenInclude(m => m!.User)
            .Where(e => e.ClubMember!.User!.Nickname == playerNickname && e.ClubId == clubId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<List<LatestHistoryEntryProjection>> ReadLatestHistoryEntryProjectionsByClubIdAsync(Guid clubId, CancellationToken cancellationToken = default)
    {
        // GroupBy(UserId).Select(g => g.OrderByDescending(...).First()) doesn't translate on
        // EF Core 10 / Npgsql; fall back to the correlated-subquery pattern (now correctly
        // scoped by ClubId so a later entry in another club can't shadow the result).
        return await dbContext.ClubMemberHistoryEntries
            .AsNoTracking()
            .Where(e => e.ClubId == clubId)
            .Where(e => e.Timestamp == dbContext.ClubMemberHistoryEntries
                .Where(ei => ei.UserId == e.UserId && ei.ClubId == clubId)
                .Max(ei => ei.Timestamp))
            .Select(e => new LatestHistoryEntryProjection(e.UserId, e.Xp, e.Timestamp))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<List<ClubMemberHistoryEntry>> ReadHistoryEntriesByClubIdAsync(Guid clubId, CancellationToken cancellationToken = default)
    {
        return await dbContext.ClubMemberHistoryEntries
            .AsNoTracking()
            .Include(e => e.ClubMember)
            .ThenInclude(m => m!.User)
            .Where(e => e.ClubId == clubId)
            .OrderByDescending(e => e.Timestamp)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<List<HistoryEntryProjection>> ReadHistoryEntryProjectionsByClubIdAsync(Guid clubId, CancellationToken cancellationToken = default)
    {
        // History entries are immutable snapshots tagged with the club at record time, so a member who
        // has since switched or left a club still has rows with e.ClubId == clubId. The extra
        // ClubMember.ClubId == clubId check keeps the average-XP results to members CURRENTLY in the
        // club — former members must not show up in the top/bottom average-XP output.
        return await dbContext.ClubMemberHistoryEntries
            .AsNoTracking()
            .Where(e => e.ClubId == clubId && e.ClubMember!.ClubId == clubId)
            .OrderByDescending(e => e.Timestamp)
            .Select(e => new HistoryEntryProjection(
                e.UserId,
                e.Xp,
                e.Timestamp,
                e.ClubMember == null ? null : e.ClubMember.User!.Nickname,
                e.ClubMember == null ? (DateTimeOffset?)null : e.ClubMember.JoinedAt))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<int> DeleteHistoryEntriesBeforeAsync(DateTimeOffset threshold, CancellationToken cancellationToken = default)
    {
        return await dbContext.ClubMemberHistoryEntries
            .Where(e => e.Timestamp < threshold)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
