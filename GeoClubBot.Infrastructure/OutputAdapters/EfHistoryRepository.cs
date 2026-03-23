using Entities;
using Infrastructure.OutputAdapters.DataAccess;
using Microsoft.EntityFrameworkCore;
using UseCases.OutputPorts;

namespace Infrastructure.OutputAdapters;

public class EfHistoryRepository(GeoClubBotDbContext dbContext) : IHistoryRepository
{
    public List<ClubMemberHistoryEntry> CreateHistoryEntries(ICollection<ClubMemberHistoryEntry> entries)
    {
        // Add the entities
        dbContext.AddRange(entries);
        
        return entries.ToList();
    }

    public async Task<List<ClubMemberHistoryEntry>> ReadHistoryEntriesAsync(Guid clubId)
    {
        // Read the entries
        var entries = await dbContext.ClubMemberHistoryEntries
            .Where(e => e.ClubId == clubId)
            .AsNoTracking()
            .ToListAsync()
            .ConfigureAwait(false);

        return entries;
    }

    public async Task<List<ClubMemberHistoryEntry>?> ReadHistoryEntriesByPlayerNicknameAsync(string playerNickname, Guid clubId)
    {
        // Get if the player is tracked
        var playerExists = await dbContext.ClubMembers
            .Include(m => m.User)
            .AnyAsync(m => m.User!.Nickname == playerNickname)
            .ConfigureAwait(false);
        
        // If the player is not tracked
        if (!playerExists)
        {
            return null;
        }
        
        // Read the entries
        var entries = await dbContext.ClubMemberHistoryEntries
            .AsNoTracking()
            .Include(e => e.ClubMember)
            .ThenInclude(m => m!.User)
            .Where(e => e.ClubMember!.User!.Nickname == playerNickname && e.ClubId == clubId)
            .ToListAsync()
            .ConfigureAwait(false);

        return entries;
    }

    public async Task<List<ClubMemberHistoryEntry>> ReadLatestHistoryEntriesByClubIdAsync(Guid clubId)
    {
        // Read the latest entries for members of this club
        var latestEntries = await dbContext.ClubMemberHistoryEntries
            .AsNoTracking()
            .Where(e => e.ClubId == clubId)
            .Where(e => e.Timestamp == dbContext.ClubMemberHistoryEntries
                .Where(ei => ei.UserId == e.UserId)
                .Max(ei => ei.Timestamp))
            .ToListAsync()
            .ConfigureAwait(false);

        return latestEntries;
    }

    public async Task<List<ClubMemberHistoryEntry>> ReadHistoryEntriesByClubIdAsync(Guid clubId)
    {
        // Read all history entries for members of this club
        var entries = await dbContext.ClubMemberHistoryEntries
            .AsNoTracking()
            .Include(e => e.ClubMember)
            .ThenInclude(m => m!.User)
            .Where(e => e.ClubId == clubId)
            .OrderByDescending(e => e.Timestamp)
            .ToListAsync()
            .ConfigureAwait(false);

        return entries;
    }

    public async Task<int> DeleteHistoryEntriesBeforeAsync(DateTimeOffset threshold)
    {
        // Delete the entries
        var numDeleted = await dbContext.ClubMemberHistoryEntries
            .Where(e => e.Timestamp < threshold)
            .ExecuteDeleteAsync()
            .ConfigureAwait(false);

        return numDeleted;
    }
}