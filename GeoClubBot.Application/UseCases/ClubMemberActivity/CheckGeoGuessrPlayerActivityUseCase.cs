using Constants;
using Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using UseCases.InputPorts.ClubMemberActivity;
using UseCases.InputPorts.ClubMembers;
using UseCases.InputPorts.Organization;
using UseCases.InputPorts.Strikes;
using UseCases.OutputPorts;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.OutputPorts.GeoGuessr.Assemblers;
using Utilities;

namespace UseCases.UseCases.ClubMemberActivity;

public class CheckGeoGuessrPlayerActivityUseCase(
    IGeoGuessrClient geoGuessrClient,
    IUnitOfWork unitOfWork,
    IActivityStatusMessageSender activityStatusMessageSender,
    ICheckStrikeDecayUseCase checkStrikeDecayUseCase,
    IReadOrSyncClubMemberUseCase readOrSyncClubMemberUseCase,
    ICleanupUseCase cleanupUseCase,
    ISaveClubMembersUseCase saveClubMembersUseCase,
    IClubMemberActivityRewardUseCase clubMemberActivityRewardUseCase,
    IConfiguration config,
    ILogger<CheckGeoGuessrPlayerActivityUseCase> logger) : ICheckGeoGuessrPlayerActivityUseCase
{
    public async Task CheckPlayerActivityAsync()
    {
        // Check the strikes for decayed strikes and remove them
        await checkStrikeDecayUseCase.CheckStrikeDecayAsync().ConfigureAwait(false);

        // Log debug message
        logger.LogDebug("Checking player activity...");

        // Get the current members of the club
        var response = await geoGuessrClient
            .ReadClubMembersAsync(_clubId).ConfigureAwait(false);

        // Assemble the entities
        var members = ClubMemberAssembler.AssembleEntities(response, _clubId);

        // Save the club members
        await saveClubMembersUseCase.SaveClubMembersAsync(members).ConfigureAwait(false);

        // Get the latest activities
        var latestHistoryEntries = await unitOfWork.History
            .ReadLatestHistoryEntriesAsync()
            .ConfigureAwait(false);

        // Get the excuses
        var excuses = await unitOfWork.Excuses
            .ReadExcusesAsync()
            .ConfigureAwait(false);

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
        unitOfWork.History.CreateHistoryEntries(newLatestHistoryEntries.Values);

        // Build the new statuses
        var newStatuses =
            await _calculateStatusesAsync(members, latestHistoryEntries, excuses, lastActivityCheckTime, now)
                .ConfigureAwait(false);

        // Save changes
        await unitOfWork.SaveChangesAsync().ConfigureAwait(false);

        // Send the update message
        await activityStatusMessageSender
            .SendActivityStatusUpdateMessageAsync(newStatuses).ConfigureAwait(false);

        // Reward player activity
        await clubMemberActivityRewardUseCase
            .RewardMemberActivityAsync(newStatuses).ConfigureAwait(false);

        // Log debug message
        logger.LogDebug("Checking player activity done.");

        // Trigger the cleanup
        await cleanupUseCase.DoCleanupAsync().ConfigureAwait(false);
    }

    private async Task<List<ClubMemberActivityStatus>> _calculateStatusesAsync(
        List<ClubMember> members,
        IEnumerable<ClubMemberHistoryEntry> latestHistoryEntries,
        IEnumerable<ClubMemberExcuse> excuses,
        DateTimeOffset lastActivityCheckTime,
        DateTimeOffset now)
    {
        var statuses = new List<ClubMemberActivityStatus>(members.Count);

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
        foreach (var member in members)
        {
            // Calculate his new status
            var newStatus = await _calculateStatusAsync(member, latestHistoryEntriesDict, excusesDict, checkTimeRange)
                .ConfigureAwait(false);

            if (newStatus != null)
            {
                statuses.Add(newStatus);
            }
        }

        return statuses;
    }

    private async Task<ClubMemberActivityStatus?> _calculateStatusAsync(
        ClubMember member,
        Dictionary<string, ClubMemberHistoryEntry> latestActivities,
        Dictionary<string, List<ClubMemberExcuse>> excuses,
        TimeRange checkTimeRange)
    {
        // Read the member from the database
        var clubMember = await readOrSyncClubMemberUseCase.ReadOrSyncClubMemberByUserIdAsync(member.User.UserId)
            .ConfigureAwait(false);

        // If the club member could not be retrieved
        if (clubMember == null)
        {
            // Log warning
            logger.LogError($"Club member {member.User.UserId} could not be found.");
            return null;
        }

        // Get the latest activity of the player
        var latestActivity = latestActivities.GetValueOrDefault(clubMember.UserId);

        // Calculate the xp since the last update
        var xpSinceLastUpdate = member.Xp - (latestActivity?.Xp ?? 0);

        // Calculate the players target respecting excuses and joined time
        var (target, individualTargetReason) = _calculateIndividualTarget(member, checkTimeRange, excuses);

        // Calculate if the player achieved the target.
        var targetAchieved = xpSinceLastUpdate >= target;

        // Read the number of strikes of the player
        var numStrikes = await unitOfWork.Strikes.ReadNumberOfActiveStrikesByMemberUserIdAsync(clubMember.UserId)
            .ConfigureAwait(false) ?? 0;
        
        // If the player did not meet the requirement and was not excused
        if (!targetAchieved)
        {
            // Add the strike
            _addStrike(clubMember.UserId, checkTimeRange.To);
            
            // Increase the number of strikes
            numStrikes++;
        }

        // Create the status object
        return new ClubMemberActivityStatus(clubMember.User.Nickname,
            clubMember.UserId,
            targetAchieved,
            xpSinceLastUpdate,
            numStrikes,
            numStrikes > _maxNumStrikes,
            target,
            individualTargetReason);
    }

    private void _addStrike(string memberUserId, DateTimeOffset now)
    {
        // Build a new strike
        var newStrike = new ClubMemberStrike
        {
            StrikeId = Guid.NewGuid(),
            UserId = memberUserId,
            Timestamp = now,
            Revoked = false
        };

        // Add the strike
        unitOfWork.Strikes.CreateStrike(newStrike);
    }

    private (int IndividualTarget, string? IndividualTargetReason) _calculateIndividualTarget(
        ClubMember member,
        TimeRange checkTimeRange,
        Dictionary<string, List<ClubMemberExcuse>> excuses)
    {
        var isNew = false;
        var isExcused = false;
        var joinedInGracePeriod = false;

        // The list of all time ranges where the player is excused
        var blockingTimeRanges = new List<TimeRange>();

        // If the member joined since the last activity check
        if (checkTimeRange.Contains(member.JoinedAt))
        {
            // Add the not in club time range
            blockingTimeRanges.Add(checkTimeRange with { To = member.JoinedAt });
            isNew = true;

            // Get the time he is in the club
            var timeInClub = checkTimeRange.To - member.JoinedAt;

            // If he joined in the grace period
            if (timeInClub < _gracePeriod)
            {
                joinedInGracePeriod = true;
            }
        }

        // Try to get the excuses of the player
        excuses.TryGetValue(member.User.UserId, out var memberExcuses);
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
        var individualTargetReason = _buildTargetReasons(isNew, isExcused, joinedInGracePeriod);

        // Get the final individual target
        var individualTarget = joinedInGracePeriod ? 0 : (int)Math.Floor(freePercent * _xpRequirement);

        return (individualTarget, individualTargetReason);
    }

    private static string? _buildTargetReasons(bool isNew, bool isExcused, bool joinedInGracePeriod)
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

        if (joinedInGracePeriod)
        {
            individualTargetReasons.Add("Joined in grace period");
        }

        var individualTargetReason = string.Join(", ", individualTargetReasons);

        return individualTargetReason;
    }

    private readonly int _xpRequirement = config.GetValue<int>(ConfigKeys.ActivityCheckerMinXpConfigurationKey);

    private readonly TimeSpan _gracePeriod = TimeSpan.FromDays(
        config.GetValue<int>(ConfigKeys.ActivityCheckerGracePeriodDaysConfigurationKey));

    private readonly int _maxNumStrikes = config.GetValue<int>(ConfigKeys.ActivityCheckerMaxNumStrikesConfigurationKey);

    private readonly Guid _clubId = config.GetValue<Guid>(ConfigKeys.GeoGuessrClubIdConfigurationKey);
}