using Entities;
using UseCases.Abstractions;
using UseCases.OutputPorts.Repositories;

namespace UseCases.UseCases.Excuses;

/// <param name="MemberUserId">The member whose excuses to read; every excuse when null.</param>
public sealed record ReadExcusesQuery(string? MemberUserId = null) : IQuery<List<ClubMemberExcuse>>;

public sealed class ReadExcusesHandler(IExcusesRepository excuses)
    : MediatR.IRequestHandler<ReadExcusesQuery, List<ClubMemberExcuse>>
{
    public Task<List<ClubMemberExcuse>> Handle(ReadExcusesQuery request, CancellationToken cancellationToken) =>
        request.MemberUserId is null
            ? excuses.ReadExcusesAsync(cancellationToken)
            : excuses.ReadExcusesByMemberUserIdAsync(request.MemberUserId, cancellationToken);
}
