using Entities;
using MediatR;
using UseCases.Abstractions;
using UseCases.OutputPorts.Repositories;
using Utilities;

namespace UseCases.UseCases.Excuses;

public sealed record ReadRelevantExcusesQuery(int UpcomingExcusesNumDays) : IQuery<Result<List<ClubMemberRelevantExcuse>>>;

public sealed class ReadRelevantExcusesHandler(IExcusesRepository repo) : IRequestHandler<ReadRelevantExcusesQuery, Result<List<ClubMemberRelevantExcuse>>>
{
    public async Task<Result<List<ClubMemberRelevantExcuse>>> Handle(ReadRelevantExcusesQuery request, CancellationToken cancellationToken)
    {
        var excuses = await repo
            .ReadAllRelevantExcusesAsync(request.UpcomingExcusesNumDays, cancellationToken)
            .ConfigureAwait(false);
        return Result<List<ClubMemberRelevantExcuse>>.Success(excuses);
    }
}
