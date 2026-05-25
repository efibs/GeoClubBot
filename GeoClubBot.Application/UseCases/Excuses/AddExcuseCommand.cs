using Entities;
using MediatR;
using UseCases.Abstractions;
using UseCases.OutputPorts;
using UseCases.UseCases.ClubMembers;

namespace UseCases.UseCases.Excuses;

public sealed record AddExcuseCommand(string MemberNickname, DateTimeOffset From, DateTimeOffset To) : ICommand<Guid?>;

public sealed class AddExcuseHandler(
    ISender mediator,
    IExcusesRepository excuses) : IRequestHandler<AddExcuseCommand, Guid?>
{
    public async Task<Guid?> Handle(AddExcuseCommand request, CancellationToken cancellationToken)
    {
        var clubMember = await mediator
            .Send(new ReadOrSyncClubMemberByNicknameQuery(request.MemberNickname), cancellationToken)
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
