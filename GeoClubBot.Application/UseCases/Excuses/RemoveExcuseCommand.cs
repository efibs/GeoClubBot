using UseCases.Abstractions;
using UseCases.OutputPorts;

namespace UseCases.UseCases.Excuses;

public sealed record RemoveExcuseCommand(Guid ExcuseId) : ICommand<bool>;

public sealed class RemoveExcuseHandler(IExcusesRepository excuses)
    : MediatR.IRequestHandler<RemoveExcuseCommand, bool>
{
    public async Task<bool> Handle(RemoveExcuseCommand request, CancellationToken cancellationToken)
    {
        var excuse = await excuses.ReadForUpdateByIdAsync(request.ExcuseId, cancellationToken).ConfigureAwait(false);

        if (excuse is null)
        {
            return false;
        }

        excuses.DeleteExcuse(excuse);
        return true;
    }
}
