using Entities;
using UseCases.Abstractions;
using UseCases.OutputPorts;

namespace UseCases.UseCases.Strikes;

public sealed record ReadAllStrikesQuery : IQuery<List<ClubMemberStrike>>;

public sealed class ReadAllStrikesHandler(IStrikesRepository strikes)
    : MediatR.IRequestHandler<ReadAllStrikesQuery, List<ClubMemberStrike>>
{
    public Task<List<ClubMemberStrike>> Handle(ReadAllStrikesQuery request, CancellationToken cancellationToken) =>
        strikes.ReadAllStrikesAsync();
}
