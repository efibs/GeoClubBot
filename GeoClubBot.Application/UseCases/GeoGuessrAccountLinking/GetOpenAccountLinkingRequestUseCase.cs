using Entities;
using UseCases.InputPorts.GeoGuessrAccountLinking;
using UseCases.OutputPorts;

namespace UseCases.UseCases.GeoGuessrAccountLinking;

public class GetOpenAccountLinkingRequestUseCase(IUnitOfWork unitOfWork) : IGetOpenAccountLinkingRequestUseCase
{
    public async Task<GeoGuessrAccountLinkingRequest?> GetOpenAccountLinkingRequestForDiscordUserIdAsync(ulong discordUserId)
    {
        // Read the linking request
        var linkingRequest = await unitOfWork.AccountLinkingRequests.ReadRequestAsync(discordUserId).ConfigureAwait(false);

        return linkingRequest;
    }
}