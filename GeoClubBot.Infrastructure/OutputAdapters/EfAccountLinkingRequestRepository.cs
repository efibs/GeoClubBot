using Entities;
using Infrastructure.OutputAdapters.DataAccess;
using Microsoft.EntityFrameworkCore;
using UseCases.OutputPorts;

namespace Infrastructure.OutputAdapters;

public class EfAccountLinkingRequestRepository(GeoClubBotDbContext dbContext) : IAccountLinkingRequestRepository
{
    public GeoGuessrAccountLinkingRequest CreateRequest(GeoGuessrAccountLinkingRequest request)
    {
        // Add the request
        dbContext.Add(request);

        return request;
    }

    public async Task<GeoGuessrAccountLinkingRequest?> ReadRequestAsync(ulong discordUserId, string geoGuessrUserId)
    {
        // Try to find the request
        var request = await dbContext.GeoGuessrAccountLinkingRequests
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.DiscordUserId == discordUserId && r.GeoGuessrUserId == geoGuessrUserId)
            .ConfigureAwait(false);
        
        return request;
    }

    public void DeleteRequest(GeoGuessrAccountLinkingRequest request)
    {
        // Delete the entity
        dbContext.GeoGuessrAccountLinkingRequests.Remove(request);
    }
}