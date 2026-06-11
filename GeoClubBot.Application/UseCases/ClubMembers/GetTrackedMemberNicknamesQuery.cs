using MediatR;
using UseCases.Abstractions;
using UseCases.OutputPorts.Repositories;

namespace UseCases.UseCases.ClubMembers;

/// <summary>
/// Returns the nicknames of all tracked club members, ordered alphabetically. Used by the
/// member-nickname autocomplete; backed by a projection read so no full entity graphs load.
/// </summary>
public sealed record GetTrackedMemberNicknamesQuery : IQuery<IReadOnlyList<string>>;

public sealed class GetTrackedMemberNicknamesHandler(IClubMemberRepository clubMembers)
    : IRequestHandler<GetTrackedMemberNicknamesQuery, IReadOnlyList<string>>
{
    public async Task<IReadOnlyList<string>> Handle(GetTrackedMemberNicknamesQuery request, CancellationToken cancellationToken) =>
        await clubMembers.ReadAllNicknamesAsync(cancellationToken).ConfigureAwait(false);
}
