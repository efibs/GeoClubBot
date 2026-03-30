using Entities;

namespace UseCases.OutputPorts;

public interface IAccountLinkingRequestRepository
{
    GeoGuessrAccountLinkingRequest CreateRequest(GeoGuessrAccountLinkingRequest request);
    
    Task<GeoGuessrAccountLinkingRequest?> ReadRequestAsync(ulong discordUserId, string geoGuessrUserId);
    
    Task<GeoGuessrAccountLinkingRequest?> ReadRequestAsync(ulong discordUserId);
    
    void DeleteRequest(GeoGuessrAccountLinkingRequest request);
}