using Constants;
using Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using UseCases.InputPorts;
using UseCases.OutputPorts;
using UseCases.OutputPorts.GeoGuessr;
using Utilities;

namespace UseCases;

public class CheckGeoGuessrPlayerActivityUseCase(
    IGeoGuessrAccess geoGuessrAccess,
    IHistoryRepository historyRepository,
    IActivityStatusMessageSender activityStatusMessageSender,
    IExcusesRepository excusesRepository,
    ICleanupUseCase cleanupUseCase,
    IConfiguration config,
    ILogger<CheckGeoGuessrPlayerActivityUseCase> logger) : ICheckGeoGuessrPlayerActivityUseCase
{
    public async Task CheckPlayerActivityAsync()
    {
        // Log debug message
        logger.LogDebug("Checking player activity...");

        // Get the current members of the club
        var members = await geoGuessrAccess
            .ReadClubMembersAsync(_clubId);

        // Get the latest activities
        var latestActivities = await historyRepository
            .ReadLatestActivityEntriesAsync();

        // Get the statuses
        var previousStatuses = await historyRepository
            .ReadActivityStatusesAsync();

        // Get the excuses
        var excuses = await excusesRepository.ReadExcusesAsync();

        // Get the last activity check time
        var lastActivityCheckTime = latestActivities.Any()
            ? latestActivities.Values.Select(a => a.Timestamp).Max()
            : DateTimeOffset.MinValue;

        // Get the current date
        var now = DateTimeOffset.UtcNow;

        // Build the time range of the check interval
        var checkTimeRange = new TimeRange(lastActivityCheckTime, now);

        // Create the new latest activity for the players
        var newLatestActivity =
            members.ToDictionary(m => m.User.UserId,
                m => new ClubMemberHistoryEntry(m.User.Nick, m.Xp, now));

        // Calculate the new statuses
        var newStatuses = members
            .ToDictionary(m => m.User.UserId,
                m => _calculateStatus(m, latestActivities, excuses, previousStatuses, checkTimeRange));

        // Save the new activity
        await historyRepository
            .WriteActivityEntriesAsync(newLatestActivity);

        // Save the new statuses
        await historyRepository
            .WriteMemberStatusesAsync(newStatuses);

        // Send the update message
        await activityStatusMessageSender
            .SendActivityStatusUpdateMessageAsync(newStatuses.Values.ToList());

        // Log debug message
        logger.LogDebug("Checking player activity done.");
        
        // Trigger the cleanup
        await cleanupUseCase.DoCleanupAsync();
    }

    private ClubMemberActivityStatus _calculateStatus(GeoGuessrClubMember member,
        Dictionary<string, ClubMemberHistoryEntry> latestActivities,
        Dictionary<string, List<ClubMemberExcuse>> excuses,
        Dictionary<string, ClubMemberActivityStatus> previousStatuses,
        TimeRange checkTimeRange)
    {
        // Get the latest activity of the player
        var latestActivity = latestActivities.GetValueOrDefault(member.User.UserId);

        // Calculate the xp since the last update
        var xpSinceLastUpdate = member.Xp - (latestActivity?.Xp ?? 0);

        // Check if the player has an excuse
        var playerHasExcuse = _hasExcuse(member.User.Nick, checkTimeRange, excuses);

        // Calculate if the player achieved the target.
        // Give new player the benefit of the doubt and say, they 
        // achieved the target since we don't know when they joined.
        var targetAchieved = latestActivity == null || xpSinceLastUpdate >= _xpRequirement;

        // Get the previous status
        var previousStatus = previousStatuses.GetValueOrDefault(member.User.UserId);

        // Calculate the new number of strikes
        var newNumberStrikes = previousStatus?.NumStrikes ?? 0;

        // If the player did not meet the requirement and was not excused
        if (!targetAchieved && !playerHasExcuse)
        {
            // Add a strike
            newNumberStrikes++;
        }

        // Create the status object
        return new ClubMemberActivityStatus(member.User.Nick, targetAchieved, playerHasExcuse,
            xpSinceLastUpdate,
            newNumberStrikes, newNumberStrikes > _maxNumStrikes, DateTimeOffset.UtcNow);
    }

    private bool _hasExcuse(string memberNickname,
        TimeRange checkTimeRange,
        Dictionary<string, List<ClubMemberExcuse>> excuses)
    {
        // Try to get the excuses of the player
        var excusesFound = excuses.TryGetValue(memberNickname, out var playerExcuses);

        // If no excuses were found
        if (!excusesFound || playerExcuses == null)
        {
            // The player is not excused
            return false;
        }

        // Try to find any excuse that intersects with the check time range
        var isExcused = playerExcuses.Any(e => checkTimeRange.Intersects(new TimeRange(e.From, e.To)));

        return isExcused;
    }
    
    private readonly int _xpRequirement = config.GetValue<int>(ConfigKeys.ActivityCheckerMinXpConfigurationKey);
    private readonly int _maxNumStrikes = config.GetValue<int>(ConfigKeys.ActivityCheckerMaxNumStrikesConfigurationKey);
    private readonly Guid _clubId = config.GetValue<Guid>(ConfigKeys.GeoGuessrClubIdConfigurationKey);
}