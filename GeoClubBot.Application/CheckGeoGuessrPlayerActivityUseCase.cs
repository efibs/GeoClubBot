using Constants;
using Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using UseCases.InputPorts;
using UseCases.OutputPorts;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.OutputPorts.GeoGuessr.DTOs;
using Utilities;

namespace UseCases;

public class CheckGeoGuessrPlayerActivityUseCase(
    IGeoGuessrAccess geoGuessrAccess,
    IHistoryRepository historyRepository,
    IActivityStatusMessageSender activityStatusMessageSender,
    IExcusesRepository excusesRepository,
    IStrikesRepository strikesRepository,
    ICheckStrikeDecayUseCase checkStrikeDecayUseCase,
    IReadOrSyncClubMemberUseCase readOrSyncClubMemberUseCase,
    ICleanupUseCase cleanupUseCase,
    IClubMemberRepository clubMemberRepository, 
    IConfiguration config,
    ILogger<CheckGeoGuessrPlayerActivityUseCase> logger) : ICheckGeoGuessrPlayerActivityUseCase
{
    public async Task CheckPlayerActivityAsync()
    {
        // Check the strikes for decayed strikes and remove them
        await checkStrikeDecayUseCase.CheckStrikeDecayAsync();
        
        // Log debug message
        logger.LogDebug("Checking player activity...");

        // Get the current members of the club
        var members = await geoGuessrAccess
            .ReadClubMembersAsync(_clubId);

        // For every member of the club
        foreach (var geoGuessrClubMember in members)
        {
            // Create the member entity
            var member = new ClubMember
            {
                UserId = geoGuessrClubMember.User.UserId,
                ClubId = _clubId,
                Nickname = geoGuessrClubMember.User.Nick,
            };
            
            // Ensure the member exists and is up to date
            await clubMemberRepository.CreateOrUpdateClubMemberAsync(member);
        }
        
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
            members.ToDictionary(m => m.User.UserId,
                m => new ClubMemberHistoryEntry
                {
                    Timestamp = now,
                    UserId = m.User.UserId,
                    Xp = m.Xp
                });

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
            var newStatus = await _calculateStatusAsync(member, latestHistoryEntriesDict, excusesDict, checkTimeRange);

            if (newStatus != null)
            {
                statuses.Add(newStatus);
            }
        }
        
        return statuses;
    }

    private async Task<ClubMemberActivityStatus?> _calculateStatusAsync(
        GeoGuessrClubMemberDTO memberDto,
        Dictionary<string, ClubMemberHistoryEntry> latestActivities,
        Dictionary<string, List<ClubMemberExcuse>> excuses,
        TimeRange checkTimeRange)
    {
        // Read the member from the database
        var clubMember = await readOrSyncClubMemberUseCase.ReadOrSyncClubMemberByUserIdAsync(memberDto.User.UserId);
        
        // If the club member could not be retrieved
        if (clubMember == null)
        {
            // Log warning
            logger.LogError($"Club member {memberDto.User.UserId} could not be found.");
            return null;
        }
        
        // Get the latest activity of the player
        var latestActivity = latestActivities.GetValueOrDefault(clubMember!.UserId);

        // Calculate the xp since the last update
        var xpSinceLastUpdate = memberDto.Xp - (latestActivity?.Xp ?? 0);

        // Calculate the players target respecting excuses and joined time
        var (target, individualTargetReason) = _calculateIndividualTarget(memberDto, checkTimeRange, excuses);
        
        // Calculate if the player achieved the target.
        var targetAchieved = xpSinceLastUpdate >= target;

        // If the player did not meet the requirement and was not excused
        if (!targetAchieved)
        {
            // Add the strike
            await _addStrikeAsync(clubMember.UserId, checkTimeRange.To);
        }

        // Read the number of strikes of the player
        var numStrikes = await strikesRepository.ReadNumberOfActiveStrikesByMemberUserIdAsync(clubMember.UserId) ?? 0;
        
        // Create the status object
        return new ClubMemberActivityStatus(clubMember.Nickname, targetAchieved,
            xpSinceLastUpdate,
            numStrikes, 
            numStrikes > _maxNumStrikes,
            target,
            individualTargetReason);
    }

    private async Task _addStrikeAsync(string memberUserId, DateTimeOffset now)
    {
        ClubMemberStrike? createdStrike = null;
        var numTries = 0;

        while (createdStrike == null && numTries++ < _createStrikeMaxRetryCount)
        {
            // Build a new strike
            var newStrike = new ClubMemberStrike
            {
                StrikeId = Guid.NewGuid(),
                UserId = memberUserId,
                Timestamp = now
            };
            
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

    private (int IndividualTarget, string? IndividualTargetReason) _calculateIndividualTarget(
        GeoGuessrClubMemberDTO memberDto, 
        TimeRange checkTimeRange,
        Dictionary<string, List<ClubMemberExcuse>> excuses)
    {
        var isNew = false;
        var isExcused = false;
        
        // The list of all time ranges where the player is excused
        var blockingTimeRanges = new List<TimeRange>();
        
        // If the member joined since the last activity check
        if (checkTimeRange.Contains(memberDto.JoinedAt))
        {
            // Add the not in club time range
            blockingTimeRanges.Add(checkTimeRange with { To = memberDto.JoinedAt });
            isNew = true;
        }
        
        // Try to get the excuses of the player
        excuses.TryGetValue(memberDto.User.UserId, out var memberExcuses);
        memberExcuses ??= [];
        
        // Calculate the intersections between the check time range and the
        // excuses
        var excuseIntersections = memberExcuses
            .Select(e => new TimeRange(e.From, e.To))
            .Where(e => checkTimeRange.Intersects(e))
            .Select(e => checkTimeRange & e)
            .ToList();
        
        // If there are excuses
        if (excuseIntersections.Any())
        {
            isExcused = true;
        }
        
        // Add the excuses
        blockingTimeRanges.AddRange(excuseIntersections);
        
        // Calculate the free percent
        var freePercent = checkTimeRange.CalculateFreePercent(blockingTimeRanges);

        // Build the individual target reason
        var individualTargetReason = _buildTargetReasons(isNew, isExcused);
        
        return ((int)Math.Floor(freePercent * _xpRequirement), individualTargetReason);
    }

    private static string? _buildTargetReasons(bool isNew, bool isExcused)
    {
        if (!isNew && !isExcused)
        {
            return null;
        }
        
        var individualTargetReasons = new List<string>();
        if (isNew)
        {
            individualTargetReasons.Add("New member");
        }

        if (isExcused)
        {
            individualTargetReasons.Add("Excused");
        }
        
        var individualTargetReason = string.Join(", ", individualTargetReasons);

        return individualTargetReason;
    }
    
    private readonly int _xpRequirement = config.GetValue<int>(ConfigKeys.ActivityCheckerMinXpConfigurationKey);
    private readonly int _maxNumStrikes = config.GetValue<int>(ConfigKeys.ActivityCheckerMaxNumStrikesConfigurationKey);
    private readonly int _createStrikeMaxRetryCount =
        config.GetValue<int>(ConfigKeys.ActivityCheckerCreateStrikeMaxRetryCountConfigurationKey);
    private readonly Guid _clubId = config.GetValue<Guid>(ConfigKeys.GeoGuessrClubIdConfigurationKey);
}