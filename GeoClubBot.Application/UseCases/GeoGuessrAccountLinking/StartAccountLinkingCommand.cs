using Constants;
using Entities;
using MediatR;
using PasswordGenerator;
using UseCases.Abstractions;
using UseCases.OutputPorts.Repositories;
using Utilities;

namespace UseCases.UseCases.GeoGuessrAccountLinking;

public sealed record StartAccountLinkingCommand(ulong DiscordUserId, string GeoGuessrUserId) : ICommand<Result<string>>;

public sealed class StartAccountLinkingHandler(IAccountLinkingRequestRepository requests)
    : IRequestHandler<StartAccountLinkingCommand, Result<string>>
{
    private static readonly Password OneTimePasswordGenerator = new(
        includeLowercase: true,
        includeUppercase: true,
        includeNumeric: true,
        includeSpecial: false,
        passwordLength: StringLengthConstants.AccountLinkingRequestOneTimePasswordLength);

    public async Task<Result<string>> Handle(StartAccountLinkingCommand request, CancellationToken cancellationToken)
    {
        var existing = await requests
            .ReadRequestAsync(request.DiscordUserId, cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            return Error.Conflict(
                "account_linking.in_progress",
                "An account-linking request is already in progress for this Discord user.");
        }

        var oneTimePassword = OneTimePasswordGenerator.Next();

        var linkingRequest = GeoGuessrAccountLinkingRequest.Create(
            request.DiscordUserId, request.GeoGuessrUserId, oneTimePassword);

        requests.AddRequest(linkingRequest);

        return oneTimePassword;
    }
}
