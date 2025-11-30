using Entities;
using Infrastructure.OutputAdapters.DataAccess;
using Microsoft.EntityFrameworkCore;
using UseCases.OutputPorts;

namespace Infrastructure.OutputAdapters;

public class EfExcusesRepository(GeoClubBotDbContext dbContext) : IExcusesRepository
{
    public ClubMemberExcuse CreateExcuse(ClubMemberExcuse excuse)
    {
        // Add the excuse
        dbContext.Add(excuse);

        return excuse;
    }

    public async Task<List<ClubMemberExcuse>> ReadExcusesAsync()
    {
        // Get all the excuses
        var excuses = await dbContext.ClubMemberExcuses
            .AsNoTracking()
            .Include(e => e.ClubMember)
            .ToListAsync()
            .ConfigureAwait(false);
        
        return excuses;
    }

    public async Task<ClubMemberExcuse?> ReadExcuseAsync(Guid id)
    {
        // Get the excuse
        var excuse = await dbContext.ClubMemberExcuses
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.ExcuseId == id)
            .ConfigureAwait(false);
        
        return excuse;
    }

    public async Task<List<ClubMemberExcuse>> ReadExcusesByMemberNicknameAsync(string memberNickname)
    {
        // Read the member
        var member = await dbContext.ClubMembers
            .AsNoTracking()
            .Include(clubMember => clubMember.Excuses)
            .Include(m => m.User)
            .FirstOrDefaultAsync(m => m.User!.Nickname == memberNickname)
            .ConfigureAwait(false);

        return member?.Excuses ?? [];
    }

    public async Task<ClubMemberExcuse?> UpdateExcuseAsync(Guid excuseId, DateTimeOffset newFrom, DateTimeOffset newTo)
    {
        // Try to find the excuse
        var existingExcuse = await dbContext.ClubMemberExcuses.FindAsync(excuseId).ConfigureAwait(false);
        
        // If the excuse was not found
        if (existingExcuse == null)
        {
            return null;
        }
        
        // Update the time range
        existingExcuse.From = newFrom;
        existingExcuse.To = newTo;
        
        return existingExcuse;
    }

    public void DeleteExcuse(ClubMemberExcuse excuse)
    {
        // Delete the entity
        dbContext.ClubMemberExcuses.Remove(excuse);
    }

    public async Task<int> DeleteExcusesBeforeAsync(DateTimeOffset threshold)
    {
        // Delete the entities
        var numDeleted = await dbContext.ClubMemberExcuses
            .Where(e => e.To < threshold)
            .ExecuteDeleteAsync()
            .ConfigureAwait(false);

        return numDeleted;
    }
}