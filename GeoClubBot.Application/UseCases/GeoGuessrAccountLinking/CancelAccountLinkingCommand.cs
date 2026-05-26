using MediatR;
using UseCases.Abstractions;
using UseCases.OutputPorts;

namespace UseCases.UseCases.GeoGuessrAccountLinking;

public sealed record CancelAccountLinkingCommand(ulong DiscordUserId, string GeoGuessrUserId) : ICommand<bool>;

public sealed class CancelAccountLinkingHandler(IAccountLinkingRequestRepository requests)
    : IRequestHandler<CancelAccountLinkingCommand, bool>
{
    public async Task<bool> Handle(CancelAccountLinkingCommand request, CancellationToken cancellationToken)
    {
        var linkingRequest = await requests
            .ReadRequestAsync(request.DiscordUserId, request.GeoGuessrUserId)
            .ConfigureAwait(false);

        if (linkingRequest is null)
        {
            return false;
        }

        requests.DeleteRequest(linkingRequest);
        return true;
    }
}
