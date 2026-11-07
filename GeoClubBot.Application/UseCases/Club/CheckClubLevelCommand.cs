using Configuration;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.Abstractions;
using UseCases.OutputPorts.GeoGuessr;

namespace UseCases.UseCases.Club;

/// <summary>
/// Reads the clubs' levels from GeoGuessr and hands each to <see cref="ApplyClubLevelCommand"/>.
/// </summary>
/// <param name="ClubId">Only this club; <c>null</c> for every configured club.</param>
public sealed record CheckClubLevelCommand(Guid? ClubId = null) : ICommand;

public sealed partial class CheckClubLevelHandler(
    IGeoGuessrClientFactory geoGuessrClientFactory,
    IServiceScopeFactory scopeFactory,
    IOptions<GeoGuessrConfiguration> geoGuessrConfig,
    ILogger<CheckClubLevelHandler> logger) : IRequestHandler<CheckClubLevelCommand, Unit>
{
    public async Task<Unit> Handle(CheckClubLevelCommand request, CancellationToken cancellationToken)
    {
        var clubIds = geoGuessrConfig.Value.Clubs
            .Select(c => c.ClubId)
            .Where(id => request.ClubId is null || id == request.ClubId);

        LogCheckingClubLevels(logger);

        // Per-club fan-out: each branch gets its own DI scope so the EF tracked-entity
        // update (UpdateLevel + SaveChanges) doesn't share a DbContext across branches.
        await Task.WhenAll(clubIds.Select(clubId => CheckClubAsync(clubId, cancellationToken)))
            .ConfigureAwait(false);

        return Unit.Value;
    }

    private async Task CheckClubAsync(Guid clubId, CancellationToken cancellationToken)
    {
        var client = geoGuessrClientFactory.CreateClient(clubId);
        var clubDto = await client.ReadClubAsync(clubId, cancellationToken).ConfigureAwait(false);

        await using var scope = scopeFactory.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ISender>()
            .Send(new ApplyClubLevelCommand(clubId, clubDto.Level), cancellationToken)
            .ConfigureAwait(false);
    }

    [LoggerMessage(LogLevel.Debug, "Checking club levels...")]
    static partial void LogCheckingClubLevels(ILogger<CheckClubLevelHandler> logger);
}
