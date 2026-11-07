using Entities;
using Infrastructure.OutputAdapters.DataAccess;
using Microsoft.EntityFrameworkCore;
using UseCases.OutputPorts.Repositories;

namespace Infrastructure.OutputAdapters.Repositories;

public class EfMemberPrivateChannelCategoryRepository(GeoClubBotDbContext dbContext) : IMemberPrivateChannelCategoryRepository
{
    public async Task<List<MemberPrivateChannelCategory>> ReadAllAsync(CancellationToken cancellationToken = default)
    {
        return await dbContext.MemberPrivateChannelCategories
            .AsNoTracking()
            .OrderBy(c => c.ClubId)
            .ThenBy(c => c.Index)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public void Add(MemberPrivateChannelCategory category)
    {
        dbContext.MemberPrivateChannelCategories.Add(category);
    }

    public async Task DeleteAsync(ulong categoryId, CancellationToken cancellationToken = default)
    {
        await dbContext.MemberPrivateChannelCategories
            .Where(c => c.CategoryId == categoryId)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
