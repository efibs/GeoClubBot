using Microsoft.Extensions.Logging;
using UseCases.Abstractions;
using UseCases.OutputPorts.Repositories;

namespace UseCases.UseCases.Strikes;

/// <summary>
/// Revokes every strike that is currently active. Returns the number of strikes revoked.
/// Revoking rather than deleting keeps the history, so single strikes can still be restored
/// with <see cref="UnrevokeStrikeCommand"/>.
/// </summary>
public sealed record RevokeAllStrikesCommand : ICommand<int>;

public sealed partial class RevokeAllStrikesHandler(
    IStrikesRepository strikes,
    ILogger<RevokeAllStrikesHandler> logger)
    : MediatR.IRequestHandler<RevokeAllStrikesCommand, int>
{
    public async Task<int> Handle(RevokeAllStrikesCommand request, CancellationToken cancellationToken)
    {
        var activeStrikes = await strikes.ReadAllActiveForUpdateAsync(cancellationToken).ConfigureAwait(false);

        foreach (var strike in activeStrikes)
        {
            strike.Revoke();
        }

        LogAllStrikesRevoked(logger, activeStrikes.Count);
        return activeStrikes.Count;
    }

    [LoggerMessage(LogLevel.Warning, "Revoked all {Count} active strikes.")]
    static partial void LogAllStrikesRevoked(ILogger<RevokeAllStrikesHandler> logger, int count);
}
