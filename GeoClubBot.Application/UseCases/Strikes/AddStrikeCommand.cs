using Entities;
using MediatR;
using UseCases.Abstractions;
using UseCases.OutputPorts;
using UseCases.UseCases.ClubMembers;

namespace UseCases.UseCases.Strikes;

public sealed record AddStrikeCommand(string MemberNickname, DateTimeOffset StrikeDate) : ICommand<Guid?>;

public sealed class AddStrikeHandler(
    ISender mediator,
    IStrikesRepository strikes) : IRequestHandler<AddStrikeCommand, Guid?>
{
    public async Task<Guid?> Handle(AddStrikeCommand request, CancellationToken cancellationToken)
    {
        var clubMember = await mediator
            .Send(new ReadOrSyncClubMemberByNicknameQuery(request.MemberNickname), cancellationToken)
            .ConfigureAwait(false);

        if (clubMember is null)
        {
            return null;
        }

        var strike = ClubMemberStrike.Create(clubMember.UserId, request.StrikeDate);
        strikes.CreateStrike(strike);

        return strike.StrikeId;
    }
}
