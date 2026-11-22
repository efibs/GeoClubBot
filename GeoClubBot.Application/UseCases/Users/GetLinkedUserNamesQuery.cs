using MediatR;
using UseCases.Abstractions;
using UseCases.OutputPorts.Projections;
using UseCases.OutputPorts.Repositories;

namespace UseCases.UseCases.Users;

/// <summary>
/// Returns the user id and nickname of every linked GeoGuessr user, ordered by nickname. Used by the
/// linked-user autocomplete; backed by a projection read so no full entity graphs load.
/// </summary>
public sealed record GetLinkedUserNamesQuery : IQuery<IReadOnlyList<UserNameProjection>>;

public sealed class GetLinkedUserNamesHandler(IGeoGuessrUserRepository users)
    : IRequestHandler<GetLinkedUserNamesQuery, IReadOnlyList<UserNameProjection>>
{
    public async Task<IReadOnlyList<UserNameProjection>> Handle(GetLinkedUserNamesQuery request, CancellationToken cancellationToken) =>
        await users.ReadAllLinkedUserNamesAsync(cancellationToken).ConfigureAwait(false);
}
