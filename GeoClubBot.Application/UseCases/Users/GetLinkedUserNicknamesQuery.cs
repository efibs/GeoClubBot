using MediatR;
using UseCases.Abstractions;
using UseCases.OutputPorts.Repositories;

namespace UseCases.UseCases.Users;

/// <summary>
/// Returns the nicknames of all linked GeoGuessr users, ordered alphabetically. Used by the
/// linked-user autocomplete; backed by a projection read so no full entity graphs load.
/// </summary>
public sealed record GetLinkedUserNicknamesQuery : IQuery<IReadOnlyList<string>>;

public sealed class GetLinkedUserNicknamesHandler(IGeoGuessrUserRepository users)
    : IRequestHandler<GetLinkedUserNicknamesQuery, IReadOnlyList<string>>
{
    public async Task<IReadOnlyList<string>> Handle(GetLinkedUserNicknamesQuery request, CancellationToken cancellationToken) =>
        await users.ReadAllLinkedNicknamesAsync(cancellationToken).ConfigureAwait(false);
}
