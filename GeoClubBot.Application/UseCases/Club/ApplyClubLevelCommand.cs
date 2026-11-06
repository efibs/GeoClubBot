using Configuration;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.Abstractions;
using UseCases.OutputPorts.Notifications;
using UseCases.OutputPorts.Repositories;

namespace UseCases.UseCases.Club;

/// <summary>
/// Takes a club's current level, however it was learnt — read by the scheduled
/// <see cref="CheckClubLevelCommand"/> or pushed over GeoGuessr's socket — and acts on a change: the
/// main club's level is the bot's status, and a level-up is announced. The first level seen for a
/// club only seeds the tracker, since there is nothing to compare it with.
/// </summary>
public sealed record ApplyClubLevelCommand(Guid ClubId, int Level) : ICommand;

public sealed partial class ApplyClubLevelHandler(
    IClubRepository clubs,
    IClubLevelTracker tracker,
    ISender sender,
    IUnitOfWork unitOfWork,
    IEnumerable<IClubEventNotifier> notifiers,
    IOptions<GeoGuessrConfiguration> geoGuessrConfig,
    ILogger<ApplyClubLevelHandler> logger) : IRequestHandler<ApplyClubLevelCommand, Unit>
{
    public async Task<Unit> Handle(ApplyClubLevelCommand request, CancellationToken cancellationToken)
    {
        var configuredClubs = geoGuessrConfig.Value.Clubs;

        // A pushed level can arrive before the first scheduled check has seeded the tracker.
        await tracker
            .EnsureInitializedAsync(clubs, configuredClubs.Select(c => c.ClubId), cancellationToken)
            .ConfigureAwait(false);

        using var clubLock = await tracker.AcquireAsync(request.ClubId, cancellationToken).ConfigureAwait(false);

        var newLevel = request.Level;
        var lastLevel = tracker.TryGet(request.ClubId);
        if (newLevel == lastLevel)
        {
            return Unit.Value;
        }

        LogClubLevelChanged(request.ClubId, newLevel);

        if (request.ClubId == geoGuessrConfig.Value.MainClub.ClubId)
        {
            await sender
                .Send(new SetClubLevelStatusCommand(newLevel), cancellationToken)
                .ConfigureAwait(false);
        }

        // Skip the "level up" notification when we're just seeding the tracker for the first time.
        if (lastLevel is not null)
        {
            var tracked = await clubs
                .ReadForUpdateByIdAsync(request.ClubId, cancellationToken)
                .ConfigureAwait(false);
            if (tracked is null)
            {
                LogClubDoesNotExist(request.ClubId);
            }
            else
            {
                tracked.UpdateLevel(newLevel);
                await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                foreach (var notifier in notifiers)
                {
                    await notifier.SendClubLevelUpEvent(tracked, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        tracker.Set(request.ClubId, newLevel);
        return Unit.Value;
    }

    [LoggerMessage(LogLevel.Debug, "Club {ClubId} level changed to {ClubLevel}")]
    partial void LogClubLevelChanged(Guid clubId, int clubLevel);

    [LoggerMessage(LogLevel.Warning, "Failed to update club level. Club {ClubId} does not exist.")]
    partial void LogClubDoesNotExist(Guid clubId);
}
