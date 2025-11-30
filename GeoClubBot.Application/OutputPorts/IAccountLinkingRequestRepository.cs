using Entities;

namespace UseCases.OutputPorts;

public interface IAccountLinkingRequestRepository
{
    GeoGuessrAccountLinkingRequest CreateRequest(GeoGuessrAccountLinkingRequest request);
    
    Task<GeoGuessrAccountLinkingRequest?> ReadRequestAsync(ulong discordUserId, string geoGuessrUserId);
    
    void DeleteRequest(GeoGuessrAccountLinkingRequest request);
}