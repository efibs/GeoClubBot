using Constants;
using Entities;
using PasswordGenerator;
using UseCases.InputPorts.GeoGuessrAccountLinking;
using UseCases.OutputPorts;

namespace UseCases.UseCases.GeoGuessrAccountLinking;

public class StartAccountLinkingUseCase(IMessageSender messageSender,
    IAccountLinkingRequestRepository accountLinkingRequestRepository) : IStartAccountLinkingProcessUseCase
{
    public async Task<string?> StartLinkingProcessAsync(ulong discordUserId, string geoGuessrUserId)
    {
        // Create the request
        var request = new GeoGuessrAccountLinkingRequest
        {
            DiscordUserId = discordUserId,
            GeoGuessrUserId = geoGuessrUserId,
            OneTimePassword = OneTimePasswordGenerator.Next()
        };
        
        // Create the linking request
        var createdRequest = await accountLinkingRequestRepository.CreateRequestAsync(request);
        
        return createdRequest?.OneTimePassword;
    }

    private static readonly Password OneTimePasswordGenerator = new Password(includeLowercase: true,
        includeUppercase: true, includeNumeric: true, includeSpecial: false,
        passwordLength: StringLengthConstants.AccountLinkingRequestOneTimePasswordLength);
}