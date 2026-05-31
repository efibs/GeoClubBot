using Entities;
using Infrastructure.OutputAdapters.DataAccess;
using Microsoft.EntityFrameworkCore;
using UseCases.OutputPorts.Repositories;

namespace Infrastructure.OutputAdapters.Repositories;

public class EfAccountLinkingRequestRepository(GeoClubBotDbContext dbContext) : IAccountLinkingRequestRepository
{
    public void AddRequest(GeoGuessrAccountLinkingRequest request)
    {
        dbContext.Add(request);
    }

    public async Task<GeoGuessrAccountLinkingRequest?> ReadRequestAsync(ulong discordUserId, string geoGuessrUserId, CancellationToken cancellationToken = default)
    {
        return await dbContext.GeoGuessrAccountLinkingRequests
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.DiscordUserId == discordUserId && r.GeoGuessrUserId == geoGuessrUserId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<GeoGuessrAccountLinkingRequest?> ReadRequestAsync(ulong discordUserId, CancellationToken cancellationToken = default)
    {
        return await dbContext.GeoGuessrAccountLinkingRequests
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.DiscordUserId == discordUserId, cancellationToken)
            .ConfigureAwait(false);
    }

    public void DeleteRequest(GeoGuessrAccountLinkingRequest request)
    {
        dbContext.GeoGuessrAccountLinkingRequests.Remove(request);
    }
}
