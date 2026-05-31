using Infrastructure.OutputAdapters.DataAccess;
using UseCases.OutputPorts.Repositories;

namespace Infrastructure.OutputAdapters.Repositories;

public class DbUnitOfWork(GeoClubBotDbContext dbContext) : IUnitOfWork
{
    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
