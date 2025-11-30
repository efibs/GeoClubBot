using UseCases.InputPorts.GeoGuessrAccountLinking;
using UseCases.OutputPorts;

namespace UseCases.UseCases.GeoGuessrAccountLinking;

public class CancelAccountLinkingUseCase(IUnitOfWork unitOfWork) : ICancelAccountLinkingUseCase
{
    public async Task<bool> CancelAccountLinkingAsync(ulong discordUserId, string geoGuessrUserId)
    {
        // Try to read teh account linking request
        var request = await unitOfWork.AccountLinkingRequests
            .ReadRequestAsync(discordUserId, geoGuessrUserId)
            .ConfigureAwait(false);
        
        // If the request was not found
        if (request == null)
        {
            return false;
        }

        // Remove the request
        unitOfWork.AccountLinkingRequests.DeleteRequest(request);
        
        // Save the changes
        await unitOfWork.SaveChangesAsync().ConfigureAwait(false);
        
        return true;
    }
}