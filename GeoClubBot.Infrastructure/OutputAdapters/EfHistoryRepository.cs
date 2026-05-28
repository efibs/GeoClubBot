using Entities;
using Infrastructure.OutputAdapters.DataAccess;
using Microsoft.EntityFrameworkCore;
using UseCases.OutputPorts;
using UseCases.OutputPorts.Projections;

namespace Infrastructure.OutputAdapters;

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

    public async Task<List<ClubMemberHistoryEntry>> ReadLatestHistoryEntriesByClubIdAsync(Guid clubId, CancellationToken cancellationToken = default)
    {
        return await dbContext.ClubMemberHistoryEntries
            .AsNoTracking()
            .Where(e => e.ClubId == clubId)
            .Where(e => e.Timestamp == dbContext.ClubMemberHistoryEntries
                .Where(ei => ei.UserId == e.UserId)
                .Max(ei => ei.Timestamp))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<List<LatestHistoryEntryProjection>> ReadLatestHistoryEntryProjectionsByClubIdAsync(Guid clubId, CancellationToken cancellationToken = default)
    {
        return await dbContext.ClubMemberHistoryEntries
            .AsNoTracking()
            .Where(e => e.ClubId == clubId)
            .GroupBy(e => e.UserId)
            .Select(g => g.OrderByDescending(e => e.Timestamp).First())
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
        return await dbContext.ClubMemberHistoryEntries
            .AsNoTracking()
            .Where(e => e.ClubId == clubId)
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
