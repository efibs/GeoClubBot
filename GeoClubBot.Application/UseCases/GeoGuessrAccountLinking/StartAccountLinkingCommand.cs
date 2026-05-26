using Constants;
using Entities;
using MediatR;
using PasswordGenerator;
using UseCases.Abstractions;
using UseCases.OutputPorts;

namespace UseCases.UseCases.GeoGuessrAccountLinking;

public sealed record StartAccountLinkingCommand(ulong DiscordUserId, string GeoGuessrUserId) : ICommand<string?>;

public sealed class StartAccountLinkingHandler(IAccountLinkingRequestRepository requests)
    : IRequestHandler<StartAccountLinkingCommand, string?>
{
    private static readonly Password OneTimePasswordGenerator = new(
        includeLowercase: true,
        includeUppercase: true,
        includeNumeric: true,
        includeSpecial: false,
        passwordLength: StringLengthConstants.AccountLinkingRequestOneTimePasswordLength);

    public Task<string?> Handle(StartAccountLinkingCommand request, CancellationToken cancellationToken)
    {
        var oneTimePassword = OneTimePasswordGenerator.Next();

        var linkingRequest = GeoGuessrAccountLinkingRequest.Create(
            request.DiscordUserId, request.GeoGuessrUserId, oneTimePassword);

        requests.AddRequest(linkingRequest);

        return Task.FromResult<string?>(oneTimePassword);
    }
}
