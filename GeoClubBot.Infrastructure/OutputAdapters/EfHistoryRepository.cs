using Entities;
using Infrastructure.OutputAdapters.DataAccess;
using Microsoft.EntityFrameworkCore;
using UseCases.OutputPorts;

namespace Infrastructure.OutputAdapters;

public class EfHistoryRepository(GeoClubBotDbContext dbContext) : IHistoryRepository
{
    public async Task<bool> CreateHistoryEntriesAsync(IEnumerable<ClubMemberHistoryEntry> entries)
    {
        // Add the entities
        dbContext.AddRange(entries);

        // Save the changes to the database
        await dbContext.SaveChangesAsync().ConfigureAwait(false);

        return true;
    }

    public async Task<List<ClubMemberHistoryEntry>> ReadHistoryEntriesAsync()
    {
        // Read the entries
        var entries = await dbContext.ClubMemberHistoryEntries.ToListAsync().ConfigureAwait(false);

        return entries;
    }

    public async Task<List<ClubMemberHistoryEntry>?> ReadHistoryEntriesByPlayerNicknameAsync(string playerNickname)
    {
        // Get if the player is tracked
        var playerExists = await dbContext.ClubMembers
            .Include(m => m.User)
            .AnyAsync(m => m.User!.Nickname == playerNickname).ConfigureAwait(false);
        
        // If the player is not tracked
        if (!playerExists)
        {
            return null;
        }
        
        // Read the entries
        var entries = await dbContext.ClubMemberHistoryEntries
            .Include(e => e.ClubMember)
            .ThenInclude(m => m!.User)
            .Where(e => e.ClubMember!.User!.Nickname == playerNickname)
            .ToListAsync().ConfigureAwait(false);

        return entries;
    }

    public async Task<List<ClubMemberHistoryEntry>> ReadLatestHistoryEntriesAsync()
    {
        // Read the latest entries
        var latestEntries = await dbContext.ClubMemberHistoryEntries
            .Where(e => e.Timestamp == dbContext.ClubMemberHistoryEntries.Max(ei => ei.Timestamp))
            .ToListAsync().ConfigureAwait(false);

        return latestEntries;
    }

    public async Task<int> DeleteHistoryEntriesBeforeAsync(DateTimeOffset threshold)
    {
        // Delete the entries
        var numDeleted = await dbContext.ClubMemberHistoryEntries
            .Where(e => e.Timestamp < threshold)
            .ExecuteDeleteAsync().ConfigureAwait(false);

        return numDeleted;
    }
}