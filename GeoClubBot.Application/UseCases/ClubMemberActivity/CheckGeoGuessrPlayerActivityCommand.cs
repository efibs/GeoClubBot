using Configuration;
using Entities;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.Abstractions;
using UseCases.OutputPorts;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.OutputPorts.GeoGuessr.Assemblers;
using UseCases.UseCases.ClubMembers;
using UseCases.UseCases.Strikes;
using Utilities;

namespace UseCases.UseCases.ClubMemberActivity;

public sealed record CheckGeoGuessrPlayerActivityCommand(Guid ClubId)
    : ICommand<List<ClubMemberActivityStatus>>;

public sealed partial class CheckGeoGuessrPlayerActivityHandler(
    IGeoGuessrClientFactory geoGuessrClientFactory,
    IStrikesRepository strikes,
    IExcusesRepository excuses,
    IClubRepository clubs,
    IClubMemberRepository clubMembers,
    IHistoryRepository history,
    IActivityStatusMessageSender activityStatusMessageSender,
    ISender mediator,
    IOptions<GeoGuessrConfiguration> geoGuessrConfig,
    IOptions<ActivityCheckerConfiguration> activityCheckerConfig,
    ILogger<CheckGeoGuessrPlayerActivityHandler> logger)
    : IRequestHandler<CheckGeoGuessrPlayerActivityCommand, List<ClubMemberActivityStatus>>
{
    public async Task<List<ClubMemberActivityStatus>> Handle(CheckGeoGuessrPlayerActivityCommand request, CancellationToken cancellationToken)
    {
        var clubId = request.ClubId;
        var clubEntry = geoGuessrConfig.Value.GetClub(clubId);
        var defaults = activityCheckerConfig.Value;
        var xpRequirement = clubEntry.GetMinXP(defaults);
        var gracePeriod = TimeSpan.FromDays(clubEntry.GetGracePeriodDays(defaults));
        var maxNumStrikes = clubEntry.GetMaxNumStrikes(defaults);

        await mediator.Send(new CheckStrikeDecayCommand(), cancellationToken).ConfigureAwait(false);

        logger.LogDebug("Checking player activity for club {ClubId}...", clubId);

        var client = geoGuessrClientFactory.CreateClient(clubId);
        var response = await client.ReadClubMembersAsync(clubId, cancellationToken).ConfigureAwait(false);
        var members = ClubMemberAssembler.AssembleEntities(response, clubId);

        var snapshots = members
            .Select(m => new ClubMemberSyncSnapshot(m.UserId, m.User.Nickname, clubId, m.Xp, m.JoinedAt))
            .ToList();
        await mediator.Send(new SaveClubMembersCommand(snapshots), cancellationToken).ConfigureAwait(false);

        var latestHistoryEntries = await history
            .ReadLatestHistoryEntriesByClubIdAsync(clubId, cancellationToken)
            .ConfigureAwait(false);

        var allExcuses = await excuses.ReadExcusesAsync(cancellationToken).ConfigureAwait(false);

        var lastActivityCheckTime = latestHistoryEntries.Any()
            ? latestHistoryEntries.Select(a => a.Timestamp).Max()
            : DateTimeOffset.MinValue;

        logger.LogInformation("Last activity check was on {LastActivityCheckTime:F}", lastActivityCheckTime);

        var now = DateTimeOffset.UtcNow;

        var newLatestHistoryEntries = members.ToDictionary(
            m => m.User.UserId,
            m => ClubMemberHistoryEntry.Create(m.User.UserId, clubId, m.Xp, now));

        history.CreateHistoryEntries(newLatestHistoryEntries.Values);

        var newStatuses = await CalculateStatusesAsync(
                members, latestHistoryEntries, allExcuses, lastActivityCheckTime, now,
                xpRequirement, gracePeriod, maxNumStrikes, cancellationToken)
            .ConfigureAwait(false);

        var club = await clubs.ReadClubByIdAsync(clubId, cancellationToken).ConfigureAwait(false);
        var clubName = club?.Name ?? clubId.ToString();

        await activityStatusMessageSender
            .SendActivityStatusUpdateMessageAsync(newStatuses, clubName, xpRequirement, cancellationToken)
            .ConfigureAwait(false);

        var averageXpTopN = clubEntry.GetAverageXpTopN(defaults);
        var averageXpBottomN = clubEntry.GetAverageXpBottomN(defaults);

        if (averageXpTopN.HasValue || averageXpBottomN.HasValue)
        {
            var historyDepth = clubEntry.GetAverageXpHistoryDepth(defaults);
            var averageXpResults = await mediator
                .Send(new CalculateAverageXpQuery(clubId, historyDepth), cancellationToken)
                .ConfigureAwait(false);

            var topMembers = averageXpTopN.HasValue
                ? averageXpResults
                    .OrderByDescending(m => m.AverageXp)
                    .ThenBy(m => m.JoinedAt)
                    .Take(averageXpTopN.Value).ToList()
                : [];

            var topNicknames = topMembers.Select(m => m.Nickname).ToHashSet();
            var bottomMembers = averageXpBottomN.HasValue
                ? averageXpResults
                    .Where(m => !topNicknames.Contains(m.Nickname))
                    .OrderBy(m => m.AverageXp)
                    .ThenByDescending(m => m.JoinedAt)
                    .Take(averageXpBottomN.Value)
                    .ToList()
                : [];

            if (topMembers.Count > 0 || bottomMembers.Count > 0)
            {
                await activityStatusMessageSender
                    .SendAverageXpMessageAsync(topMembers, bottomMembers, clubName, historyDepth, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        logger.LogDebug("Checking player activity for club {ClubId} done.", clubId);

        return newStatuses;
    }

    private async Task<List<ClubMemberActivityStatus>> CalculateStatusesAsync(
        List<ClubMember> members,
        IEnumerable<ClubMemberHistoryEntry> latestHistoryEntries,
        IEnumerable<ClubMemberExcuse> excusesList,
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
        var userIds = members.Select(m => m.User.UserId).ToList();
        var persistedMembersTask = clubMembers.ReadClubMembersByUserIdsAsync(userIds, cancellationToken);
        var activeStrikeCountsTask = strikes.ReadActiveStrikeCountsByMemberUserIdsAsync(userIds, cancellationToken);
        await Task.WhenAll(persistedMembersTask, activeStrikeCountsTask).ConfigureAwait(false);
        var persistedMembers = await persistedMembersTask.ConfigureAwait(false);
        var activeStrikeCounts = await activeStrikeCountsTask.ConfigureAwait(false);

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
        Dictionary<string, ClubMemberHistoryEntry> latestActivities,
        Dictionary<string, List<ClubMemberExcuse>> excusesDict,
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
        Dictionary<string, List<ClubMemberExcuse>> excuses,
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
    static partial void LogClubMemberCouldNotBeFound(ILogger<CheckGeoGuessrPlayerActivityHandler> logger, string memberUserId);
}
