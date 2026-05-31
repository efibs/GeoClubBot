using Entities;
using MediatR;
using UseCases.Abstractions;
using UseCases.OutputPorts.Repositories;
using UseCases.UseCases.ClubMembers;
using Utilities;

namespace UseCases.UseCases.Strikes;

public sealed record AddStrikeCommand(string MemberNickname, DateTimeOffset StrikeDate) : ICommand<Result<Guid>>;

public sealed class AddStrikeHandler(
    ISender mediator,
    IStrikesRepository strikes) : IRequestHandler<AddStrikeCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(AddStrikeCommand request, CancellationToken cancellationToken)
    {
        var memberResult = await mediator
            .Send(new ReadOrSyncClubMemberByNicknameQuery(request.MemberNickname), cancellationToken)
            .ConfigureAwait(false);

        if (memberResult.IsFailure)
        {
            return memberResult.Error;
        }

        var strike = ClubMemberStrike.Create(memberResult.Value.UserId, request.StrikeDate);
        strikes.CreateStrike(strike);

        return strike.StrikeId;
    }
}
