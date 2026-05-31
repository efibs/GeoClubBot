using Entities;
using UseCases.Abstractions;
using UseCases.OutputPorts.Repositories;
using Utilities;

namespace UseCases.UseCases.Excuses;

public sealed record UpdateExcuseCommand(Guid ExcuseId, DateTimeOffset From, DateTimeOffset To) : ICommand<Result<ClubMemberExcuse>>;

public sealed class UpdateExcuseHandler(IExcusesRepository excuses)
    : MediatR.IRequestHandler<UpdateExcuseCommand, Result<ClubMemberExcuse>>
{
    public async Task<Result<ClubMemberExcuse>> Handle(UpdateExcuseCommand request, CancellationToken cancellationToken)
    {
        var excuse = await excuses.ReadForUpdateByIdAsync(request.ExcuseId, cancellationToken).ConfigureAwait(false);

        if (excuse is null)
        {
            return Error.NotFound("excuse.not_found", $"Excuse '{request.ExcuseId}' does not exist.");
        }

        excuse.UpdateTimeRange(request.From, request.To);
        return excuse;
    }
}
