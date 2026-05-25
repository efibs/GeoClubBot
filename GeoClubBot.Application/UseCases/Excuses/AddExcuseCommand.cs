using Entities;
using UseCases.Abstractions;
using UseCases.InputPorts.ClubMembers;
using UseCases.OutputPorts;

namespace UseCases.UseCases.Excuses;

public sealed record AddExcuseCommand(string MemberNickname, DateTimeOffset From, DateTimeOffset To) : ICommand<Guid?>;

public sealed class AddExcuseHandler(
    IReadOrSyncClubMemberUseCase readClubMemberUseCase,
    IExcusesRepository excuses) : MediatR.IRequestHandler<AddExcuseCommand, Guid?>
{
    public async Task<Guid?> Handle(AddExcuseCommand request, CancellationToken cancellationToken)
    {
        var clubMember = await readClubMemberUseCase
            .ReadOrSyncClubMemberByNicknameAsync(request.MemberNickname)
            .ConfigureAwait(false);

        if (clubMember is null)
        {
            return null;
        }

        var excuse = ClubMemberExcuse.Create(clubMember.UserId, request.From, request.To);
        excuses.CreateExcuse(excuse);

        return excuse.ExcuseId;
    }
}
