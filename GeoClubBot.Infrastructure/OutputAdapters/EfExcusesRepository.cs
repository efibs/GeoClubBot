using Entities;
using Infrastructure.OutputAdapters.DataAccess;
using Microsoft.EntityFrameworkCore;
using UseCases.OutputPorts;

namespace Infrastructure.OutputAdapters;

public class EfExcusesRepository(GeoClubBotDbContext dbContext) : IExcusesRepository
{
    public async Task<ClubMemberExcuse?> CreateExcuseAsync(ClubMemberExcuse excuse)
    {
        // Try to find an existing excuse with that id
        var excuseExists = await dbContext.ClubMemberExcuses.AnyAsync(e => e.Id == excuse.Id);

        // If the club member already exists
        if (excuseExists)
        {
            return null;
        }

        // Add the excuse
        dbContext.Add(excuse);

        // Save the changes to the database
        await dbContext.SaveChangesAsync();

        return excuse;
    }

    public async Task<List<ClubMemberExcuse>> ReadExcusesAsync()
    {
        // Get all the excuses
        var excuses = await dbContext.ClubMemberExcuses.ToListAsync();
        
        return excuses;
    }

    public async Task<List<ClubMemberExcuse>> ReadExcusesByMemberNicknameAsync(string memberNickname)
    {
        // Read the member
        var member = await dbContext.ClubMembers
            .Include(clubMember => clubMember.Excuses)
            .FirstOrDefaultAsync(m => m.Nickname == memberNickname);

        return member?.Excuses ?? [];
    }

    public async Task<bool> DeleteExcuseByIdAsync(Guid excuseId)
    {
        // Find the excuse
        var excuse = await dbContext.ClubMemberExcuses.FindAsync(excuseId);
        
        // If the excuse was not found
        if (excuse == null)
        {
            return false;
        }
        
        // Delete the entity
        dbContext.Remove(excuse);
        
        return true;
    }

    public async Task<int> DeleteExcusesBeforeAsync(DateTimeOffset threshold)
    {
        // Delete the entities
        var numDeleted = await dbContext.ClubMemberExcuses
            .Where(e => e.To < threshold)
            .ExecuteDeleteAsync();

        return numDeleted;
    }
}