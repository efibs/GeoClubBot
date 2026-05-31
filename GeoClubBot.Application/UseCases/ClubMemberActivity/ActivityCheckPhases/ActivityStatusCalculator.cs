using Entities;
using Microsoft.Extensions.Logging;
using UseCases.OutputPorts.Projections;
using UseCases.OutputPorts.Repositories;
using Utilities;

namespace UseCases.UseCases.ClubMemberActivity.ActivityCheckPhases;

/// <summary>
/// Second phase of <see cref="CheckGeoGuessrPlayerActivityHandler"/>: turn pre-fetched
/// API members + history + excuses into a list of <see cref="ClubMemberActivityStatus"/>,
/// creating new strikes for members below their individual XP target.
/// </summary>
public sealed partial class ActivityStatusCalculator(
    IStrikesRepository strikes,
    IClubMemberRepository clubMembers,
    ILogger<ActivityStatusCalculator> logger)
{
    public async Task<List<ClubMemberActivityStatus>> ExecuteAsync(
        List<ClubMember> members,
        IEnumerable<LatestHistoryEntryProjection> latestHistoryEntries,
        IEnumerable<ExcuseProjection> excusesList,
        DateTimeOffset lastActivityCheckTime,
        DateTimeOffset now,
        int xpRequirement,
        TimeSpan gracePeriod,
        int maxNumStrikes,
        CancellationToken cancellationToken)
    {
        var statuses = new List<ClubMemberActivityStatus>(members.Count);

        var latestHistoryEntriesDict = latestHistoryEntries.ToDictionary(e => e.UserId, e => e);

        var excusesDict = excusesList
            .GroupBy(e => e.UserId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var checkTimeRange = new TimeRange(lastActivityCheckTime, now);

        // SaveClubMembersCommand has just persisted every API member, so a single batched
        // read returns each ClubMember + active strike count. The hot loop below is pure
        // dict lookups; no more per-member DB round-trip.
        //
        // These two reads share the request-scoped DbContext, so they MUST be awaited
        // sequentially — EF Core does not support concurrent operations on a single context
        // (running them via Task.WhenAll throws "A second operation was started on this context
        // instance...", which surfaces under the parallel multi-club activity check).
        var userIds = members.Select(m => m.User.UserId).ToList();
        var persistedMembers = await clubMembers
            .ReadClubMembersByUserIdsAsync(userIds, cancellationToken)
            .ConfigureAwait(false);
        var activeStrikeCounts = await strikes
            .ReadActiveStrikeCountsByMemberUserIdsAsync(userIds, cancellationToken)
            .ConfigureAwait(false);

        foreach (var member in members)
        {
            var newStatus = CalculateStatus(
                member, latestHistoryEntriesDict, excusesDict, persistedMembers, activeStrikeCounts,
                checkTimeRange, xpRequirement, gracePeriod, maxNumStrikes);

            if (newStatus is not null)
            {
                statuses.Add(newStatus);
            }
        }

        return statuses;
    }

    private ClubMemberActivityStatus? CalculateStatus(
        ClubMember member,
        Dictionary<string, LatestHistoryEntryProjection> latestActivities,
        Dictionary<string, List<ExcuseProjection>> excusesDict,
        Dictionary<string, ClubMember> persistedMembers,
        Dictionary<string, int> activeStrikeCounts,
        TimeRange checkTimeRange,
        int xpRequirement,
        TimeSpan gracePeriod,
        int maxNumStrikes)
    {
        if (!persistedMembers.TryGetValue(member.User.UserId, out var clubMember))
        {
            LogClubMemberCouldNotBeFound(logger, member.User.UserId);
            return null;
        }

        var latestActivity = latestActivities.GetValueOrDefault(clubMember.UserId);
        var xpSinceLastUpdate = member.Xp - (latestActivity?.Xp ?? 0);

        var (target, individualTargetReason) = CalculateIndividualTarget(
            member, checkTimeRange, excusesDict, xpRequirement, gracePeriod);

        var targetAchieved = xpSinceLastUpdate >= target;

        var numStrikes = activeStrikeCounts.GetValueOrDefault(clubMember.UserId, 0);

        if (!targetAchieved)
        {
            var newStrike = ClubMemberStrike.Create(clubMember.UserId, checkTimeRange.To);
            strikes.CreateStrike(newStrike);
            numStrikes++;
        }

        return new ClubMemberActivityStatus(
            clubMember.User.Nickname,
            clubMember.UserId,
            targetAchieved,
            xpSinceLastUpdate,
            numStrikes,
            numStrikes > maxNumStrikes,
            target,
            individualTargetReason);
    }

    private static (int IndividualTarget, string? IndividualTargetReason) CalculateIndividualTarget(
        ClubMember member,
        TimeRange checkTimeRange,
        Dictionary<string, List<ExcuseProjection>> excuses,
        int xpRequirement,
        TimeSpan gracePeriod)
    {
        var isNew = false;
        var isExcused = false;
        var joinedInGracePeriod = false;

        var blockingTimeRanges = new List<TimeRange>();

        if (checkTimeRange.Contains(member.JoinedAt))
        {
            blockingTimeRanges.Add(checkTimeRange with { To = member.JoinedAt });
            isNew = true;

            var timeInClub = checkTimeRange.To - member.JoinedAt;
            if (timeInClub < gracePeriod)
            {
                joinedInGracePeriod = true;
            }
        }

        excuses.TryGetValue(member.User.UserId, out var memberExcuses);
        memberExcuses ??= [];

        var excuseIntersections = memberExcuses
            .Select(e => new TimeRange(e.From, e.To))
            .Where(e => checkTimeRange.Intersects(e))
            .Select(e => checkTimeRange & e)
            .ToList();

        if (excuseIntersections.Any())
        {
            isExcused = true;
        }

        blockingTimeRanges.AddRange(excuseIntersections);

        var freePercent = checkTimeRange.CalculateFreePercent(blockingTimeRanges);

        var individualTargetReason = BuildTargetReasons(isNew, isExcused, joinedInGracePeriod);

        var individualTarget = joinedInGracePeriod ? 0 : (int)Math.Floor(freePercent * xpRequirement);

        return (individualTarget, individualTargetReason);
    }

    private static string? BuildTargetReasons(bool isNew, bool isExcused, bool joinedInGracePeriod)
    {
        if (!isNew && !isExcused)
        {
            return null;
        }

        var reasons = new List<string>();
        if (isNew) reasons.Add("New member");
        if (isExcused) reasons.Add("Excused");
        if (joinedInGracePeriod) reasons.Add("Joined in grace period");
        return string.Join(", ", reasons);
    }

    [LoggerMessage(LogLevel.Error, "Club member {memberUserId} could not be found.")]
    static partial void LogClubMemberCouldNotBeFound(ILogger<ActivityStatusCalculator> logger, string memberUserId);
}
