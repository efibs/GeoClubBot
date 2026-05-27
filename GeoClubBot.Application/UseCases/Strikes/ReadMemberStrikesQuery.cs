using Entities;
using UseCases.Abstractions;
using UseCases.OutputPorts;

namespace UseCases.UseCases.Strikes;

public sealed record ReadMemberStrikesQuery(string MemberNickname) : IQuery<ClubMemberStrikeStatus?>;

public sealed class ReadMemberStrikesHandler(IStrikesRepository strikes)
    : MediatR.IRequestHandler<ReadMemberStrikesQuery, ClubMemberStrikeStatus?>
{
    public async Task<ClubMemberStrikeStatus?> Handle(ReadMemberStrikesQuery request, CancellationToken cancellationToken)
    {
        var memberStrikes = await strikes
            .ReadStrikesByMemberNicknameAsync(request.MemberNickname, cancellationToken)
            .ConfigureAwait(false);

        if (memberStrikes is null)
        {
            return null;
        }

        var numActive = memberStrikes.Count(s => s.IsActive);
        return new ClubMemberStrikeStatus(numActive, memberStrikes);
    }
}
