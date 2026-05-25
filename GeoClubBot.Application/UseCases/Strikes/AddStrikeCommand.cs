using Entities;
using UseCases.Abstractions;
using UseCases.InputPorts.ClubMembers;
using UseCases.OutputPorts;

namespace UseCases.UseCases.Strikes;

public sealed record AddStrikeCommand(string MemberNickname, DateTimeOffset StrikeDate) : ICommand<Guid?>;

public sealed class AddStrikeHandler(
    IReadOrSyncClubMemberUseCase readClubMemberUseCase,
    IStrikesRepository strikes) : MediatR.IRequestHandler<AddStrikeCommand, Guid?>
{
    public async Task<Guid?> Handle(AddStrikeCommand request, CancellationToken cancellationToken)
    {
        var clubMember = await readClubMemberUseCase
            .ReadOrSyncClubMemberByNicknameAsync(request.MemberNickname)
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
