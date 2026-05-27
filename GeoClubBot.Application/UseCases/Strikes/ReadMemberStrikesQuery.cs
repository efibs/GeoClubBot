using Entities;
using UseCases.Abstractions;
using UseCases.OutputPorts;
using Utilities;

namespace UseCases.UseCases.Strikes;

public sealed record ReadMemberStrikesQuery(string MemberNickname) : IQuery<Result<ClubMemberStrikeStatus>>;

public sealed class ReadMemberStrikesHandler(IStrikesRepository strikes)
    : MediatR.IRequestHandler<ReadMemberStrikesQuery, Result<ClubMemberStrikeStatus>>
{
    public async Task<Result<ClubMemberStrikeStatus>> Handle(ReadMemberStrikesQuery request, CancellationToken cancellationToken)
    {
        var memberStrikes = await strikes
            .ReadStrikesByMemberNicknameAsync(request.MemberNickname, cancellationToken)
            .ConfigureAwait(false);

        if (memberStrikes is null)
        {
            return Error.NotFound(
                "club_member.not_found",
                $"There is no player with the nickname {request.MemberNickname} currently being tracked. " +
                "Either the nickname is incorrect or the member just joined and is not yet being tracked.");
        }

        var numActive = memberStrikes.Count(s => s.IsActive);
        return new ClubMemberStrikeStatus(numActive, memberStrikes);
    }
}
