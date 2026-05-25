using Entities;
using UseCases.Abstractions;
using UseCases.OutputPorts;

namespace UseCases.UseCases.Excuses;

public sealed record UpdateExcuseCommand(Guid ExcuseId, DateTimeOffset From, DateTimeOffset To) : ICommand<ClubMemberExcuse?>;

public sealed class UpdateExcuseHandler(IExcusesRepository excuses)
    : MediatR.IRequestHandler<UpdateExcuseCommand, ClubMemberExcuse?>
{
    public async Task<ClubMemberExcuse?> Handle(UpdateExcuseCommand request, CancellationToken cancellationToken)
    {
        var excuse = await excuses.ReadForUpdateByIdAsync(request.ExcuseId).ConfigureAwait(false);

        if (excuse is null)
        {
            return null;
        }

        excuse.UpdateTimeRange(request.From, request.To);
        return excuse;
    }
}
