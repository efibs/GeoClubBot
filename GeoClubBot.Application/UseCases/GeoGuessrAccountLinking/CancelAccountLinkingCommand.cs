using MediatR;
using UseCases.Abstractions;
using UseCases.OutputPorts.Repositories;
using Utilities;

namespace UseCases.UseCases.GeoGuessrAccountLinking;

public sealed record CancelAccountLinkingCommand(ulong DiscordUserId, string GeoGuessrUserId) : ICommand<Result>;

public sealed class CancelAccountLinkingHandler(IAccountLinkingRequestRepository requests)
    : IRequestHandler<CancelAccountLinkingCommand, Result>
{
    public async Task<Result> Handle(CancelAccountLinkingCommand request, CancellationToken cancellationToken)
    {
        var linkingRequest = await requests
            .ReadRequestAsync(request.DiscordUserId, request.GeoGuessrUserId, cancellationToken)
            .ConfigureAwait(false);

        if (linkingRequest is null)
        {
            return Error.NotFound(
                "account_linking.request_not_found",
                "There was no account-linking request for the given accounts.");
        }

        requests.DeleteRequest(linkingRequest);
        return Result.Success();
    }
}
