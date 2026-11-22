using MediatR;
using UseCases.Abstractions;
using UseCases.OutputPorts.Projections;
using UseCases.OutputPorts.Repositories;

namespace UseCases.UseCases.ClubMembers;

/// <summary>
/// Returns the user id and nickname of every tracked club member, ordered by nickname. Used by the
/// member autocomplete; backed by a projection read so no full entity graphs load.
/// </summary>
public sealed record GetTrackedMemberNamesQuery : IQuery<IReadOnlyList<UserNameProjection>>;

public sealed class GetTrackedMemberNamesHandler(IClubMemberRepository clubMembers)
    : IRequestHandler<GetTrackedMemberNamesQuery, IReadOnlyList<UserNameProjection>>
{
    public async Task<IReadOnlyList<UserNameProjection>> Handle(GetTrackedMemberNamesQuery request, CancellationToken cancellationToken) =>
        await clubMembers.ReadAllMemberNamesAsync(cancellationToken).ConfigureAwait(false);
}
