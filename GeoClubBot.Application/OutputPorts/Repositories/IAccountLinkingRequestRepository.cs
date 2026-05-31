using Entities;

namespace UseCases.OutputPorts.Repositories;

public interface IAccountLinkingRequestRepository
{
    void AddRequest(GeoGuessrAccountLinkingRequest request);

    Task<GeoGuessrAccountLinkingRequest?> ReadRequestAsync(ulong discordUserId, string geoGuessrUserId, CancellationToken cancellationToken = default);

    Task<GeoGuessrAccountLinkingRequest?> ReadRequestAsync(ulong discordUserId, CancellationToken cancellationToken = default);

    void DeleteRequest(GeoGuessrAccountLinkingRequest request);
}
