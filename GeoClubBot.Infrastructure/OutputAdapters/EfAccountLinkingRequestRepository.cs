using Entities;
using Infrastructure.OutputAdapters.DataAccess;
using Microsoft.EntityFrameworkCore;
using UseCases.OutputPorts;

namespace Infrastructure.OutputAdapters;

public class EfAccountLinkingRequestRepository(GeoClubBotDbContext dbContext) : IAccountLinkingRequestRepository
{
    public void AddRequest(GeoGuessrAccountLinkingRequest request)
    {
        dbContext.Add(request);
    }

    public async Task<GeoGuessrAccountLinkingRequest?> ReadRequestAsync(ulong discordUserId, string geoGuessrUserId)
    {
        return await dbContext.GeoGuessrAccountLinkingRequests
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.DiscordUserId == discordUserId && r.GeoGuessrUserId == geoGuessrUserId)
            .ConfigureAwait(false);
    }

    public async Task<GeoGuessrAccountLinkingRequest?> ReadRequestAsync(ulong discordUserId)
    {
        return await dbContext.GeoGuessrAccountLinkingRequests
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.DiscordUserId == discordUserId)
            .ConfigureAwait(false);
    }

    public void DeleteRequest(GeoGuessrAccountLinkingRequest request)
    {
        dbContext.GeoGuessrAccountLinkingRequests.Remove(request);
    }
}
