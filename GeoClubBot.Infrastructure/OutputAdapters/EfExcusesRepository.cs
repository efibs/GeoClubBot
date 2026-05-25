using Entities;
using Infrastructure.OutputAdapters.DataAccess;
using Microsoft.EntityFrameworkCore;
using UseCases.OutputPorts;

namespace Infrastructure.OutputAdapters;

public class EfExcusesRepository(GeoClubBotDbContext dbContext) : IExcusesRepository
{
    public ClubMemberExcuse CreateExcuse(ClubMemberExcuse excuse)
    {
        dbContext.Add(excuse);
        return excuse;
    }

    public async Task<List<ClubMemberExcuse>> ReadExcusesAsync()
    {
        return await dbContext.ClubMemberExcuses
            .AsNoTracking()
            .Include(e => e.ClubMember)
            .ThenInclude(m => m!.User)
            .ToListAsync()
            .ConfigureAwait(false);
    }

    public async Task<ClubMemberExcuse?> ReadExcuseAsync(Guid id)
    {
        return await dbContext.ClubMemberExcuses
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.ExcuseId == id)
            .ConfigureAwait(false);
    }

    public async Task<ClubMemberExcuse?> ReadForUpdateByIdAsync(Guid excuseId)
    {
        return await dbContext.ClubMemberExcuses
            .FirstOrDefaultAsync(e => e.ExcuseId == excuseId)
            .ConfigureAwait(false);
    }

    public async Task<List<ClubMemberExcuse>> ReadExcusesByMemberNicknameAsync(string memberNickname)
    {
        var member = await dbContext.ClubMembers
            .AsNoTracking()
            .Include(clubMember => clubMember.Excuses)
            .Include(m => m.User)
            .FirstOrDefaultAsync(m => m.User!.Nickname == memberNickname)
            .ConfigureAwait(false);

        return member?.Excuses ?? [];
    }

    public void DeleteExcuse(ClubMemberExcuse excuse)
    {
        dbContext.ClubMemberExcuses.Remove(excuse);
    }

    public async Task<int> DeleteExcusesBeforeAsync(DateTimeOffset threshold)
    {
        return await dbContext.ClubMemberExcuses
            .Where(e => e.To < threshold)
            .ExecuteDeleteAsync()
            .ConfigureAwait(false);
    }
}
