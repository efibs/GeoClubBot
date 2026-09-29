using Entities;
using Microsoft.Extensions.Logging;
using UseCases.OutputPorts.Projections;
using UseCases.OutputPorts.Repositories;
using UseCases.UseCases.ClubMemberActivity.Rules;
using Utilities;

namespace UseCases.UseCases.ClubMemberActivity.ActivityCheckPhases;

/// <summary>
/// Second phase of <see cref="CheckGeoGuessrPlayerActivityHandler"/>: turn pre-fetched
/// API members + history + excuses + the window's activity feed into a list of
/// <see cref="ClubMemberActivityStatus"/>, creating new strikes for members who missed one of the
/// club's requirements and recording what each member did on their new history snapshot.
/// </summary>
public sealed partial class ActivityStatusCalculator(
    IStrikesRepository strikes,
    IClubMemberRepository clubMembers,
    ILogger<ActivityStatusCalculator> logger)
{
    public async Task<List<ClubMemberActivityStatus>> ExecuteAsync(
        List<ClubMember> members,
        IEnumerable<LatestHistoryEntryProjection> latestHistoryEntries,
        IReadOnlyDictionary<string, ClubMemberHistoryEntry> newHistoryEntries,
        IEnumerable<ExcuseProjection> excusesList,
        IReadOnlyDictionary<string, List<ClubActivityEntry>> activitiesByUser,
        TimeRange checkTimeRange,
        ActivityRules rules,
        TimeSpan gracePeriod,
        int maxNumStrikes,
        CancellationToken cancellationToken)
    {
        var statuses = new List<ClubMemberActivityStatus>(members.Count);

        var latestHistoryEntriesDict = latestHistoryEntries.ToDictionary(e => e.UserId, e => e);

        var excusesDict = excusesList
            .GroupBy(e => e.UserId)
            .ToDictionary(g => g.Key, g => g.ToList());

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
                member, latestHistoryEntriesDict, newHistoryEntries, excusesDict, activitiesByUser,
                persistedMembers, activeStrikeCounts, checkTimeRange, rules, gracePeriod, maxNumStrikes);

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
        IReadOnlyDictionary<string, ClubMemberHistoryEntry> newHistoryEntries,
        Dictionary<string, List<ExcuseProjection>> excusesDict,
        IReadOnlyDictionary<string, List<ClubActivityEntry>> activitiesByUser,
        Dictionary<string, ClubMember> persistedMembers,
        Dictionary<string, int> activeStrikeCounts,
        TimeRange checkTimeRange,
        ActivityRules rules,
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

        var (targetFactor, individualTargetReason) = CalculateTargetFactor(
            member, checkTimeRange, excusesDict, gracePeriod);

        var evaluation = ActivityRuleEvaluator.Evaluate(
            activitiesByUser.GetValueOrDefault(clubMember.UserId) ?? [], rules, targetFactor);

        newHistoryEntries.GetValueOrDefault(clubMember.UserId)?.RecordInterval(
            evaluation.RuleXp,
            evaluation.CountOf(ClubXpActivityKind.DailyChallengeOrDuel),
            evaluation.CountOf(ClubXpActivityKind.BoardMission));

        var targetAchieved = evaluation.AllMet;

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
            ActivityRuleEvaluator.ScaleTarget(rules.MinRuleXp, targetFactor),
            individualTargetReason,
            evaluation.RuleXp,
            evaluation.Requirements);
    }

    /// <summary>
    /// The share of the window the member was expected to be active in: the time before they joined
    /// and any excused time is taken out, and a member inside the grace period owes nothing.
    /// </summary>
    private static (double TargetFactor, string? IndividualTargetReason) CalculateTargetFactor(
        ClubMember member,
        TimeRange checkTimeRange,
        Dictionary<string, List<ExcuseProjection>> excuses,
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

        return (joinedInGracePeriod ? 0 : freePercent, individualTargetReason);
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
