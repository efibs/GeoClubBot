using Entities;
using Infrastructure.OutputAdapters.DataAccess;
using Microsoft.EntityFrameworkCore;
using UseCases.OutputPorts;

namespace Infrastructure.OutputAdapters;

public class EfAccountLinkingRequestRepository(GeoClubBotDbContext dbContext) : IAccountLinkingRequestRepository
{
    public async Task<GeoGuessrAccountLinkingRequest?> CreateRequestAsync(GeoGuessrAccountLinkingRequest request)
    {
        // Try to find an existing request with that id
        var requestExists = await dbContext.GeoGuessrAccountLinkingRequests
            .AnyAsync(lr => lr.DiscordUserId == request.DiscordUserId && lr.GeoGuessrUserId == request.GeoGuessrUserId).ConfigureAwait(false);

        // If the request already exists
        if (requestExists)
        {
            return null;
        }
        
        // Add the request
        dbContext.Add(request);
        
        // Save the changes to the database
        await dbContext.SaveChangesAsync().ConfigureAwait(false);

        return request;
    }

    public async Task<GeoGuessrAccountLinkingRequest?> ReadRequestAsync(ulong discordUserId, string geoGuessrUserId)
    {
        // Try to find the request
        var request = await dbContext.GeoGuessrAccountLinkingRequests
            .FindAsync(discordUserId, geoGuessrUserId).ConfigureAwait(false);
        
        return request;
    }

    public async Task<bool> DeleteRequestAsync(ulong discordUserId, string geoGuessrUserId)
    {
        // Try to find the request
        var request = await dbContext.GeoGuessrAccountLinkingRequests
            .FindAsync(discordUserId, geoGuessrUserId).ConfigureAwait(false);
        
        // If the request was not found
        if (request == null)
        {
            return false;
        }
        
        // Delete the entity
        dbContext.Remove(request);
        
        // Save the changes to the database
        await dbContext.SaveChangesAsync().ConfigureAwait(false);
        
        return true;
    }
}