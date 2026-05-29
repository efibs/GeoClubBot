using Configuration;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.Abstractions;
using UseCases.OutputPorts;
using UseCases.OutputPorts.GeoGuessr;

namespace UseCases.UseCases.Club;

public sealed record CheckClubLevelCommand : ICommand;

public sealed partial class CheckClubLevelHandler(
    IGeoGuessrClientFactory geoGuessrClientFactory,
    IClubRepository clubs,
    IClubLevelTracker tracker,
    IServiceScopeFactory scopeFactory,
    IOptions<GeoGuessrConfiguration> geoGuessrConfig,
    ILogger<CheckClubLevelHandler> logger) : IRequestHandler<CheckClubLevelCommand, Unit>
{
    public async Task<Unit> Handle(CheckClubLevelCommand request, CancellationToken cancellationToken)
    {
        var configuredClubs = geoGuessrConfig.Value.Clubs;
        var mainClubId = geoGuessrConfig.Value.MainClub.ClubId;

        await tracker
            .EnsureInitializedAsync(clubs, configuredClubs.Select(c => c.ClubId), cancellationToken)
            .ConfigureAwait(false);

        LogCheckingClubLevels(logger);

        // Per-club fan-out: each branch gets its own DI scope so the EF tracked-entity
        // update (UpdateLevel + SaveChanges) doesn't share a DbContext across branches.
        // ClubLevelTracker is a thread-safe ConcurrentDictionary so the per-key Set is safe.
        await Task.WhenAll(configuredClubs.Select(async clubEntry =>
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var scopedSender = scope.ServiceProvider.GetRequiredService<ISender>();
            var scopedClubs = scope.ServiceProvider.GetRequiredService<IClubRepository>();
            var scopedUnitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var scopedNotifiers = scope.ServiceProvider.GetRequiredService<IEnumerable<IClubEventNotifier>>();

            var client = geoGuessrClientFactory.CreateClient(clubEntry.ClubId);
            var clubDto = await client.ReadClubAsync(clubEntry.ClubId, cancellationToken).ConfigureAwait(false);
            var newLevel = clubDto.Level;

            var lastLevel = tracker.TryGet(clubEntry.ClubId);
            if (newLevel == lastLevel)
            {
                return;
            }

            LogClubLevelChangedToClubLevel(newLevel);

            if (clubEntry.ClubId == mainClubId)
            {
                await scopedSender
                    .Send(new SetClubLevelStatusCommand(newLevel), cancellationToken)
                    .ConfigureAwait(false);
            }

            // Skip the "level up" notification when we're just seeding the tracker for the first time.
            if (lastLevel is not null)
            {
                var tracked = await scopedClubs
                    .ReadForUpdateByIdAsync(clubEntry.ClubId, cancellationToken)
                    .ConfigureAwait(false);
                if (tracked is null)
                {
                    LogFailedToUpdateClubLevelClubDoesNotExits(clubEntry.ClubId);
                }
                else
                {
                    tracked.UpdateLevel(newLevel);
                    await scopedUnitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                    foreach (var notifier in scopedNotifiers)
                    {
                        await notifier.SendClubLevelUpEvent(tracked, cancellationToken).ConfigureAwait(false);
                    }
                }
            }

            tracker.Set(clubEntry.ClubId, newLevel);
        })).ConfigureAwait(false);

        return Unit.Value;
    }

    [LoggerMessage(LogLevel.Debug, "Club level changed to {clubLevel}")]
    partial void LogClubLevelChangedToClubLevel(int clubLevel);

    [LoggerMessage(LogLevel.Warning, "Failed to update club level. Club {clubId} does not exits.")]
    partial void LogFailedToUpdateClubLevelClubDoesNotExits(Guid clubId);

    [LoggerMessage(LogLevel.Debug, "Checking club levels...")]
    static partial void LogCheckingClubLevels(ILogger<CheckClubLevelHandler> logger);
}
