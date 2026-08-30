using MediatR;
using UseCases.Abstractions;
using UseCases.OutputPorts.AI;
using Utilities;

namespace UseCases.UseCases.AI;

/// <summary>
/// Re-reads the provider's free-model roster into the catalog.
///
/// Runs on a schedule and at start-up because free models are added and retired continuously — a
/// model id that worked yesterday may be gone today, which is exactly what made the previous
/// hardcoded-model design unreliable.
/// </summary>
public sealed record RefreshChatModelCatalogCommand : ICommand<Result<int>>;

public sealed class RefreshChatModelCatalogHandler(IChatModelCatalog catalog)
    : IRequestHandler<RefreshChatModelCatalogCommand, Result<int>>
{
    public Task<Result<int>> Handle(RefreshChatModelCatalogCommand request, CancellationToken cancellationToken) =>
        catalog.RefreshAsync(cancellationToken);
}
