using Entities;
using UseCases.Abstractions;
using UseCases.OutputPorts.Repositories;
using Utilities;

namespace UseCases.UseCases.Strikes;

public sealed record ReadMemberStrikesQuery(string MemberUserId) : IQuery<Result<ClubMemberStrikeStatus>>;

public sealed class ReadMemberStrikesHandler(IClubMemberRepository clubMembers, IStrikesRepository strikes)
    : MediatR.IRequestHandler<ReadMemberStrikesQuery, Result<ClubMemberStrikeStatus>>
{
    private static readonly Error MemberNotFound = Error.NotFound(
        "club_member.not_found",
        "That player is not tracked by the bot. Please pick one of the suggested members; " +
        "a member who just joined is tracked after the next sync.");

    public async Task<Result<ClubMemberStrikeStatus>> Handle(ReadMemberStrikesQuery request, CancellationToken cancellationToken)
    {
        var member = await clubMembers
            .ReadClubMemberByUserIdAsync(request.MemberUserId, cancellationToken)
            .ConfigureAwait(false);

        if (member is null)
        {
            return MemberNotFound;
        }

        var memberStrikes = await strikes
            .ReadStrikesByMemberUserIdAsync(request.MemberUserId, cancellationToken)
            .ConfigureAwait(false);

        var numActive = memberStrikes.Count(s => s.IsActive);
        return new ClubMemberStrikeStatus(member.User.Nickname, numActive, memberStrikes);
    }
}
