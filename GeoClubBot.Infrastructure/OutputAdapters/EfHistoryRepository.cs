using Entities;
using Infrastructure.OutputAdapters.DataAccess;
using Microsoft.EntityFrameworkCore;
using UseCases.OutputPorts;

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

    public async Task<int> DeleteHistoryEntriesBeforeAsync(DateTimeOffset threshold, CancellationToken cancellationToken = default)
    {
        return await dbContext.ClubMemberHistoryEntries
            .Where(e => e.Timestamp < threshold)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
