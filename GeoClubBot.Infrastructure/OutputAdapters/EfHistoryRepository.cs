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

    public async Task<List<ClubMemberHistoryEntry>> ReadHistoryEntriesAsync()
    {
        // Read the entries
        var entries = await dbContext.ClubMemberHistoryEntries
            .AsNoTracking()
            .ToListAsync()
            .ConfigureAwait(false);

        return entries;
    }

    public async Task<List<ClubMemberHistoryEntry>?> ReadHistoryEntriesByPlayerNicknameAsync(string playerNickname)
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
            .Where(e => e.ClubMember!.User!.Nickname == playerNickname)
            .ToListAsync()
            .ConfigureAwait(false);

        return entries;
    }

    public async Task<List<ClubMemberHistoryEntry>> ReadLatestHistoryEntriesAsync()
    {
        // Read the latest entries
        var latestEntries = await dbContext.ClubMemberHistoryEntries
            .AsNoTracking()
            .Where(e => e.Timestamp == dbContext.ClubMemberHistoryEntries.Max(ei => ei.Timestamp))
            .ToListAsync()
            .ConfigureAwait(false);

        return latestEntries;
    }

    public async Task<List<ClubMemberHistoryEntry>> ReadLatestHistoryEntriesByClubIdAsync(Guid clubId)
    {
        // Get user IDs belonging to this club
        var clubMemberUserIds = dbContext.ClubMembers
            .Where(m => m.ClubId == clubId && m.IsCurrentlyMember)
            .Select(m => m.UserId);

        // Read the latest entries for members of this club
        var latestEntries = await dbContext.ClubMemberHistoryEntries
            .AsNoTracking()
            .Where(e => clubMemberUserIds.Contains(e.UserId))
            .Where(e => e.Timestamp == dbContext.ClubMemberHistoryEntries
                .Where(ei => clubMemberUserIds.Contains(ei.UserId))
                .Max(ei => ei.Timestamp))
            .ToListAsync()
            .ConfigureAwait(false);

        return latestEntries;
    }

    public async Task<List<ClubMemberHistoryEntry>> ReadHistoryEntriesByClubIdAsync(Guid clubId)
    {
        // Get user IDs belonging to this club
        var clubMemberUserIds = dbContext.ClubMembers
            .Where(m => m.ClubId == clubId)
            .Select(m => m.UserId);

        // Read all history entries for members of this club
        var entries = await dbContext.ClubMemberHistoryEntries
            .AsNoTracking()
            .Include(e => e.ClubMember)
            .ThenInclude(m => m!.User)
            .Where(e => clubMemberUserIds.Contains(e.UserId))
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