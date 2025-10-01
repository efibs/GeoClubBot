using UseCases.InputPorts.GeoGuessrAccountLinking;
using UseCases.OutputPorts;

namespace UseCases.UseCases.GeoGuessrAccountLinking;

public class CancelAccountLinkingUseCase(IAccountLinkingRequestRepository accountLinkingRequestRepository) : ICancelAccountLinkingUseCase
{
    public async Task<bool> CancelAccountLinkingAsync(ulong discordUserId, string geoGuessrUserId)
    {
        return await accountLinkingRequestRepository.DeleteRequestAsync(discordUserId, geoGuessrUserId).ConfigureAwait(false);
    }
}