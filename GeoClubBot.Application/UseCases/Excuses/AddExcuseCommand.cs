using Entities;
using MediatR;
using UseCases.Abstractions;
using UseCases.OutputPorts;
using UseCases.UseCases.ClubMembers;
using Utilities;

namespace UseCases.UseCases.Excuses;

public sealed record AddExcuseCommand(string MemberNickname, DateTimeOffset From, DateTimeOffset To) : ICommand<Result<Guid>>;

public sealed class AddExcuseHandler(
    ISender mediator,
    IExcusesRepository excuses) : IRequestHandler<AddExcuseCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(AddExcuseCommand request, CancellationToken cancellationToken)
    {
        var memberResult = await mediator
            .Send(new ReadOrSyncClubMemberByNicknameQuery(request.MemberNickname), cancellationToken)
            .ConfigureAwait(false);

        if (memberResult.IsFailure)
        {
            return memberResult.Error;
        }

        var excuse = ClubMemberExcuse.Create(memberResult.Value.UserId, request.From, request.To);
        excuses.CreateExcuse(excuse);

        return excuse.ExcuseId;
    }
}
