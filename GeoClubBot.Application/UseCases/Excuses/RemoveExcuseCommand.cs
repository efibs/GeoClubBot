using UseCases.Abstractions;
using UseCases.OutputPorts.Repositories;
using Utilities;

namespace UseCases.UseCases.Excuses;

public sealed record RemoveExcuseCommand(Guid ExcuseId) : ICommand<Result>;

public sealed class RemoveExcuseHandler(IExcusesRepository excuses)
    : MediatR.IRequestHandler<RemoveExcuseCommand, Result>
{
    public async Task<Result> Handle(RemoveExcuseCommand request, CancellationToken cancellationToken)
    {
        var excuse = await excuses.ReadForUpdateByIdAsync(request.ExcuseId, cancellationToken).ConfigureAwait(false);

        if (excuse is null)
        {
            return Error.NotFound("excuse.not_found", $"Excuse '{request.ExcuseId}' does not exist.");
        }

        excuses.DeleteExcuse(excuse);
        return Result.Success();
    }
}
