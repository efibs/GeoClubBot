using Entities;
using UseCases.Abstractions;
using UseCases.OutputPorts;

namespace UseCases.UseCases.Excuses;

public sealed record ReadExcusesQuery(string? MemberNickname = null) : IQuery<List<ClubMemberExcuse>>;

public sealed class ReadExcusesHandler(IExcusesRepository excuses)
    : MediatR.IRequestHandler<ReadExcusesQuery, List<ClubMemberExcuse>>
{
    public Task<List<ClubMemberExcuse>> Handle(ReadExcusesQuery request, CancellationToken cancellationToken) =>
        request.MemberNickname is null
            ? excuses.ReadExcusesAsync(cancellationToken)
            : excuses.ReadExcusesByMemberNicknameAsync(request.MemberNickname, cancellationToken);
}
