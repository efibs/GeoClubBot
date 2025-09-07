using Entities;

namespace UseCases.OutputPorts;

public interface IAccountLinkingRequestRepository
{
    Task<GeoGuessrAccountLinkingRequest?> CreateRequestAsync(GeoGuessrAccountLinkingRequest request);
    
    Task<GeoGuessrAccountLinkingRequest?> ReadRequestAsync(ulong discordUserId, string geoGuessrUserId);
    
    Task<bool> DeleteRequestAsync(ulong discordUserId, string geoGuessrUserId);
}