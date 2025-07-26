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
    IStrikesRepository strikesRepository,
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
        var latestHistoryEntries = await historyRepository
            .ReadLatestHistoryEntriesAsync();

        // Get the excuses
        var excuses = await excusesRepository.ReadExcusesAsync();

        // Get the last activity check time
        var lastActivityCheckTime = latestHistoryEntries.Any()
            ? latestHistoryEntries.Select(a => a.Timestamp).Max()
            : DateTimeOffset.MinValue;

        // Get the current date
        var now = DateTimeOffset.UtcNow;

        // Create the new latest activity for the players
        var newLatestHistoryEntries =
            members.ToDictionary(m => m.UserDto.UserId,
                m => new ClubMemberHistoryEntry(now, m.UserDto.UserId, m.Xp));

        // Save the new activity
        await historyRepository
            .CreateHistoryEntriesAsync(newLatestHistoryEntries.Values);

        // Build the new statuses
        var newStatuses = await _calculateStatusesAsync(members, latestHistoryEntries, excuses, lastActivityCheckTime, now);

        // Send the update message
        await activityStatusMessageSender
            .SendActivityStatusUpdateMessageAsync(newStatuses);

        // Log debug message
        logger.LogDebug("Checking player activity done.");

        // Trigger the cleanup
        await cleanupUseCase.DoCleanupAsync();
    }

    private async Task<List<ClubMemberActivityStatus>> _calculateStatusesAsync(
        List<GeoGuessrClubMemberDTO> memberDtos,
        IEnumerable<ClubMemberHistoryEntry> latestHistoryEntries,
        IEnumerable<ClubMemberExcuse> excuses,
        DateTimeOffset lastActivityCheckTime,
        DateTimeOffset now)
    {
        var statuses = new List<ClubMemberActivityStatus>(memberDtos.Count);
        
        // Convert the latest history entries to dictionary
        var latestHistoryEntriesDict = latestHistoryEntries
            .ToDictionary(e => e.UserId, e => e);

        // Convert excuses to dictionary
        var excusesDict = excuses
            .GroupBy(e => e.UserId)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Build the time range of the check interval
        var checkTimeRange = new TimeRange(lastActivityCheckTime, now);
        
        // For every member
        foreach (var member in memberDtos)
        {
            // Calculate his new status
            var newStatus = await _calculateStatusAsync(member, latestHistoryEntriesDict, excusesDict, checkTimeRange, now);
            statuses.Add(newStatus);
        }
        
        return statuses;
    }

    private async Task<ClubMemberActivityStatus> _calculateStatusAsync(
        GeoGuessrClubMemberDTO memberDto,
        Dictionary<string, ClubMemberHistoryEntry> latestActivities,
        Dictionary<string, List<ClubMemberExcuse>> excuses,
        TimeRange checkTimeRange,
        DateTimeOffset now)
    {
        // Get the latest activity of the player
        var latestActivity = latestActivities.GetValueOrDefault(memberDto.UserDto.UserId);

        // Calculate the xp since the last update
        var xpSinceLastUpdate = memberDto.Xp - (latestActivity?.Xp ?? 0);

        // Check if the player has an excuse
        var playerHasExcuse = _hasExcuse(memberDto.UserDto.Nick, checkTimeRange, excuses);

        // Calculate if the player achieved the target.
        // Give new player the benefit of the doubt and say, they 
        // achieved the target since we don't know when they joined.
        var targetAchieved = latestActivity == null || xpSinceLastUpdate >= _xpRequirement;

        // If the player did not meet the requirement and was not excused
        if (!targetAchieved && !playerHasExcuse)
        {
            // Add the strike
            await _addStrikeAsync(memberDto.UserDto.UserId, now);
        }

        // Read the number of strikes of the player
        var numStrikes = await strikesRepository.ReadNumberOfActiveStrikesByMemberUserIdAsync(memberDto.UserDto.UserId);
        
        // Create the status object
        return new ClubMemberActivityStatus(memberDto.UserDto.Nick, targetAchieved, playerHasExcuse,
            xpSinceLastUpdate,
            numStrikes, numStrikes > _maxNumStrikes);
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

    private async Task _addStrikeAsync(string memberUserId, DateTimeOffset now)
    {
        ClubMemberStrike? createdStrike = null;
        var numTries = 0;

        while (createdStrike == null && numTries++ < _createStrikeMaxRetryCount)
        {
            // Build a new strike
            var newStrike = new ClubMemberStrike(Guid.NewGuid(), memberUserId, now);
            
            // Add the strike
            createdStrike = await strikesRepository.CreateStrikeAsync(newStrike);
            
            // If the creation failed
            if (createdStrike == null)
            {
                // Log warning
                logger.LogWarning($"Failed to create strike: {newStrike}");
            }
        }
        
        // If the strike is still not created
        if (createdStrike == null)
        {
            // Log error
            logger.LogError($"Strike for member {memberUserId} could not be created.");
        }
    }
    
    private readonly int _xpRequirement = config.GetValue<int>(ConfigKeys.ActivityCheckerMinXpConfigurationKey);
    private readonly int _maxNumStrikes = config.GetValue<int>(ConfigKeys.ActivityCheckerMaxNumStrikesConfigurationKey);
    private readonly int _createStrikeMaxRetryCount =
        config.GetValue<int>(ConfigKeys.ActivityCheckerCreateStrikeMaxRetryCountConfigurationKey);
    private readonly Guid _clubId = config.GetValue<Guid>(ConfigKeys.GeoGuessrClubIdConfigurationKey);
}