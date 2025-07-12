using Entities;
using GeoClubBot;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using UseCases.InputPorts;
using UseCases.OutputPorts;

namespace UseCases;

public class CheckGeoGuessrPlayerActivityUseCase(
    IGeoGuessrAccess geoGuessrAccess,
    IActivityRepository activityRepository,
    IStatusMessageSender statusMessageSender,
    IConfiguration config,
    ILogger<CheckGeoGuessrPlayerActivityUseCase> logger) : ICheckGeoGuessrPlayerActivityUseCase
{
    public async Task CheckPlayerActivityAsync()
    {
        // TODO: Don't strike users that were not in the history before (new club members)
        
        // Log debug message
        logger.LogDebug("Checking player activity...");
        
        // Get the members of the club
        var members = await geoGuessrAccess
            .ReadClubMembersAsync(_clubId)
            .ConfigureAwait(false);

        // Get the latest activities
        var latestActivities = await activityRepository
            .ReadLatestActivityEntriesAsync()
            .ConfigureAwait(false);

        // Get the statuses
        var previousStatuses = await activityRepository
            .ReadActivityStatusesAsync()
            .ConfigureAwait(false);

        // Get the current date
        var now = DateTimeOffset.UtcNow;

        // Create the new latest activity for the players
        var newLatestActivity =
            members.ToDictionary(m => m.User.UserId,
                m => new GeoGuessrClubMemberActivityEntry(m.User.Nick, m.Xp, now));

        // Calculate the new statuses
        var newStatuses = members
            .ToDictionary(m => m.User.UserId, m =>
        {
            // Get the latest activity of the player
            var latestActivity = latestActivities.GetValueOrDefault(m.User.UserId);

            // Calculate the xp since the last update
            var xpSinceLastUpdate = m.Xp - (latestActivity?.Xp ?? 0);

            // Calculate if the player achieved the target
            var targetAchieved = xpSinceLastUpdate >= _xpRequirement;

            // Get the previous status
            var previousStatus = previousStatuses.GetValueOrDefault(m.User.UserId);

            // Calculate the new number of strikes
            var newNumberStrikes = (previousStatus?.NumStrikes ?? 0) + 1;

            return new GeoGuessrClubMemberActivityStatus(m.User.Nick, targetAchieved, xpSinceLastUpdate,
                newNumberStrikes, newNumberStrikes > _maxNumStrikes);
        });

        // Save the new activity
        await activityRepository
            .WriteActivityEntriesAsync(newLatestActivity)
            .ConfigureAwait(false);
        
        // Save the new statuses
        await activityRepository
            .WriteMemberStatusesAsync(newStatuses)
            .ConfigureAwait(false);
        
        // Send the update message
        await statusMessageSender
            .SendActivityStatusUpdateMessageAsync(newStatuses.Values.ToList())
            .ConfigureAwait(false);
        
        // Log debug message
        logger.LogDebug("Checking player activity done.");
    }

    private readonly int _xpRequirement = config.GetValue<int>(ConfigKeys.ActivityCheckerMinXpConfigurationKey);
    private readonly int _maxNumStrikes = config.GetValue<int>(ConfigKeys.ActivityCheckerMaxNumStrikesConfigurationKey);
    private readonly Guid _clubId = config.GetValue<Guid>(ConfigKeys.ActivityCheckerClubIdConfigurationKey);
}