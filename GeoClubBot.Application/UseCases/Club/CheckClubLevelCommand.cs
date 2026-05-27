using Configuration;
using MediatR;
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
    ISender mediator,
    IEnumerable<IClubEventNotifier> notifiers,
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

        logger.LogDebug("Checking club levels...");

        foreach (var clubEntry in configuredClubs)
        {
            var client = geoGuessrClientFactory.CreateClient(clubEntry.ClubId);
            var clubDto = await client.ReadClubAsync(clubEntry.ClubId, cancellationToken).ConfigureAwait(false);
            var newLevel = clubDto.Level;

            var lastLevel = tracker.TryGet(clubEntry.ClubId);
            if (newLevel == lastLevel)
            {
                continue;
            }

            LogClubLevelChangedToClubLevel(newLevel);

            if (clubEntry.ClubId == mainClubId)
            {
                await mediator.Send(new SetClubLevelStatusCommand(newLevel), cancellationToken).ConfigureAwait(false);
            }

            // Skip the "level up" notification when we're just seeding the tracker for the first time.
            if (lastLevel is not null)
            {
                var tracked = await clubs.ReadForUpdateByIdAsync(clubEntry.ClubId, cancellationToken).ConfigureAwait(false);
                if (tracked is null)
                {
                    LogFailedToUpdateClubLevelClubDoesNotExits(clubEntry.ClubId);
                }
                else
                {
                    tracked.UpdateLevel(newLevel);

                    foreach (var notifier in notifiers)
                    {
                        await notifier.SendClubLevelUpEvent(tracked, cancellationToken).ConfigureAwait(false);
                    }
                }
            }

            tracker.Set(clubEntry.ClubId, newLevel);
        }

        return Unit.Value;
    }

    [LoggerMessage(LogLevel.Debug, "Club level changed to {clubLevel}")]
    partial void LogClubLevelChangedToClubLevel(int clubLevel);

    [LoggerMessage(LogLevel.Warning, "Failed to update club level. Club {clubId} does not exits.")]
    partial void LogFailedToUpdateClubLevelClubDoesNotExits(Guid clubId);
}
