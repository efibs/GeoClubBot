using Entities;

namespace UseCases.OutputPorts;

public interface IAccountLinkingRequestRepository
{
    void AddRequest(GeoGuessrAccountLinkingRequest request);

    Task<GeoGuessrAccountLinkingRequest?> ReadRequestAsync(ulong discordUserId, string geoGuessrUserId);

    Task<GeoGuessrAccountLinkingRequest?> ReadRequestAsync(ulong discordUserId);

    void DeleteRequest(GeoGuessrAccountLinkingRequest request);
}
