using Entities;
using MediatR;
using UseCases.Abstractions;
using UseCases.OutputPorts.Repositories;

namespace UseCases.UseCases.GeoGuessrAccountLinking;

/// <summary>
/// Lists every open account-linking request, for the admin dashboard. Consumers must never expose
/// the requests' one-time passwords to anyone but the requesting member themself.
/// </summary>
public sealed record ReadOpenAccountLinkingRequestsQuery : IQuery<List<GeoGuessrAccountLinkingRequest>>;

public sealed class ReadOpenAccountLinkingRequestsQueryHandler(IAccountLinkingRequestRepository requests)
    : IRequestHandler<ReadOpenAccountLinkingRequestsQuery, List<GeoGuessrAccountLinkingRequest>>
{
    public Task<List<GeoGuessrAccountLinkingRequest>> Handle(
        ReadOpenAccountLinkingRequestsQuery request,
        CancellationToken cancellationToken) =>
        requests.ReadAllRequestsAsync(cancellationToken);
}
