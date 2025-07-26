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
        await dbContext.SaveChangesAsync();

        return true;
    }

    public async Task<List<ClubMemberHistoryEntry>> ReadHistoryEntriesAsync()
    {
        // Read the entries
        var entries = await dbContext.ClubMemberHistoryEntries.ToListAsync();

        return entries;
    }

    public async Task<List<ClubMemberHistoryEntry>> ReadLatestHistoryEntriesAsync()
    {
        // Read the latest entries
        var latestEntries = await dbContext.ClubMemberHistoryEntries
            .Where(e => e.Timestamp == dbContext.ClubMemberHistoryEntries.Max(ei => ei.Timestamp))
            .ToListAsync();

        return latestEntries;
    }

    public async Task<int> DeleteHistoryEntriesBeforeAsync(DateTimeOffset threshold)
    {
        // Delete the entries
        var numDeleted = await dbContext.ClubMemberHistoryEntries
            .Where(e => e.Timestamp < threshold)
            .ExecuteDeleteAsync();

        return numDeleted;
    }
}