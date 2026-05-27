using Infrastructure.OutputAdapters.DataAccess;
using UseCases.OutputPorts;

namespace Infrastructure.OutputAdapters;

public class DbUnitOfWork(GeoClubBotDbContext dbContext) : IUnitOfWork
{
    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
