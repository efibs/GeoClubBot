using Configuration;
using Entities;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.OutputPorts.Repositories;
using UseCases.UseCases.ClubMemberActivity.Rules;

namespace UseCases.UseCases.ClubMemberActivity;

public sealed partial class ActivityReadHandlers(
    IClubRepository clubs,
    IClubMemberRepository clubMembers,
    IGeoGuessrActivityReader activityReader,
    IClubMissionBoardReader boardReader,
    IMissionClaimRepository missionClaims,
    ClubActivityKindClassifier activityKinds,
    IOptions<GeoGuessrConfiguration> geoGuessrConfig,
    IOptions<ActivityCheckerConfiguration> activityCheckerConfig,
    IOptions<ActivityViewsConfiguration> viewsConfig,
    IOptions<MissionClaimTrackingConfiguration> claimTrackingConfig,
    ILogger<ActivityReadHandlers> logger)
    : IRequestHandler<GetLastCheckTimeQuery, DateTimeOffset?>,
      IRequestHandler<GetActivityThisWeekQuery, ClubMemberActivitySummary>,
      IRequestHandler<GetActivityLastDaysQuery, ClubMemberActivitySummary>
{
    private readonly Guid _mainClubId = geoGuessrConfig.Value.MainClub.ClubId;

    public async Task<DateTimeOffset?> Handle(GetLastCheckTimeQuery request, CancellationToken cancellationToken)
    {
        var club = await clubs.ReadClubByIdAsync(_mainClubId, cancellationToken).ConfigureAwait(false);
        if (club is null)
        {
            LogClubNotFound(logger, _mainClubId);
        }
        return club?.LatestActivityCheckTime;
    }

    public async Task<ClubMemberActivitySummary> Handle(GetActivityThisWeekQuery request, CancellationToken cancellationToken)
    {
        var clubMember = await clubMembers
            .ReadClubMemberByUserIdAsync(request.UserId, cancellationToken)
            .ConfigureAwait(false);

        var now = DateTimeOffset.UtcNow;

        // The period the next weekly check will judge: everything since the club's last check,
        // bounded like the check itself bounds it.
        var periodStart = now - activityCheckerConfig.Value.MaxFeedLookback;
        if (clubMember?.ClubId is { } clubId
            && await clubs.ReadClubByIdAsync(clubId, cancellationToken).ConfigureAwait(false) is { LatestActivityCheckTime: { } lastCheck }
            && lastCheck > periodStart)
        {
            periodStart = lastCheck;
        }

        return await ComputeActivityAsync(clubMember, request.UserId, periodStart, now, isCheckPeriod: true, cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<ClubMemberActivitySummary> Handle(GetActivityLastDaysQuery request, CancellationToken cancellationToken)
    {
        var daysBack = Math.Clamp(request.DaysBack, 1, viewsConfig.Value.MaxDaysBack);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var startDate = today.AddDays(-(daysBack - 1));
        var startUtc = new DateTimeOffset(startDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        return ComputeLastDaysAsync(request.UserId, startUtc, cancellationToken);
    }

    private async Task<ClubMemberActivitySummary> ComputeLastDaysAsync(
        string userId, DateTimeOffset startUtc, CancellationToken cancellationToken)
    {
        var clubMember = await clubMembers
            .ReadClubMemberByUserIdAsync(userId, cancellationToken)
            .ConfigureAwait(false);

        return await ComputeActivityAsync(clubMember, userId, startUtc, DateTimeOffset.UtcNow, isCheckPeriod: false, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<ClubMemberActivitySummary> ComputeActivityAsync(
        ClubMember? clubMember,
        string userId,
        DateTimeOffset from,
        DateTimeOffset to,
        bool isCheckPeriod,
        CancellationToken cancellationToken)
    {
        var startDate = DateOnly.FromDateTime(from.UtcDateTime);
        var endDate = DateOnly.FromDateTime(to.UtcDateTime);
        var daySlots = Enumerable.Range(0, endDate.DayNumber - startDate.DayNumber + 1)
            .Select(i => startDate.AddDays(i))
            .ToList();

        var clubId = clubMember?.ClubId;
        var memberActivities = new List<ReadClubActivitiesItemDto>();
        foreach (var feedClubId in FeedsToRead(clubId))
        {
            var activities = await activityReader
                .ReadActivitiesSinceAsync(feedClubId, from, cancellationToken)
                .ConfigureAwait(false);

            memberActivities.AddRange(activities
                .Where(a => a.UserId == userId && a.RecordedAt >= from && a.RecordedAt <= to));
        }

        var byDay = memberActivities
            .GroupBy(a => DateOnly.FromDateTime(a.RecordedAt.UtcDateTime))
            .ToDictionary(g => g.Key, g => g.ToList());

        var days = daySlots
            .Select(d =>
            {
                var dayActivities = byDay.GetValueOrDefault(d) ?? [];
                return new DayActivity(
                    d,
                    dayActivities.Any(activityKinds.IsDailyChallenge),
                    dayActivities.Count(activityKinds.IsBoardMission),
                    dayActivities.Sum(a => a.XpReward));
            })
            .ToList();

        int? ruleXp = null;
        IReadOnlyList<ActivityRequirementResult>? requirements = null;
        if (isCheckPeriod && clubId is { } checkedClubId && geoGuessrConfig.Value.Clubs.Any(c => c.ClubId == checkedClubId))
        {
            var rules = ActivityRules.Resolve(activityCheckerConfig.Value, geoGuessrConfig.Value.GetClub(checkedClubId));
            var evaluation = ActivityRuleEvaluator.Evaluate(
                ClubActivityEntries.From(memberActivities, activityKinds), rules, targetFactor: 1);
            ruleXp = evaluation.RuleXp;
            requirements = evaluation.Requirements;
        }

        // The boards are the current club's; one who left has no board any more.
        var (helpedThisWeek, helpedLastWeek) = clubId is { } boardClubId
            ? await CountHelpsAsync(boardClubId, userId, cancellationToken).ConfigureAwait(false)
            : (null, null);
        var missionCompletion = await ComputeMissionCompletionAsync(userId, from, to, cancellationToken).ConfigureAwait(false);

        return new ClubMemberActivitySummary(
            Days: days,
            TotalXp: memberActivities.Sum(a => a.XpReward),
            StreakDays: memberActivities.Count(activityKinds.IsDailyChallenge),
            BoardMissions: memberActivities.Count(activityKinds.IsBoardMission),
            BoardClearBonusXp: memberActivities.Where(activityKinds.IsBoardClearBonus).Sum(a => a.XpReward),
            // The join date of one who left is the one of the club they left: no "joined" line for them.
            JoinedInPeriod: clubId is not null && clubMember!.JoinedAt >= from,
            JoinedDateTime: clubMember?.JoinedAt ?? DateTimeOffset.UtcNow,
            RuleXp: ruleXp,
            Requirements: requirements,
            HelpedThisWeek: helpedThisWeek,
            HelpedLastWeek: helpedLastWeek,
            PeriodStart: from,
            MissionCompletion: missionCompletion);
    }

    /// <summary>
    /// The feeds holding the member's entries: their club's. One who left (or was never synced) has no
    /// club any more, yet what they did before leaving stays in the feed of the club they left, so every
    /// configured club's feed is read for them (#144).
    /// </summary>
    private IEnumerable<Guid> FeedsToRead(Guid? clubId) =>
        clubId is { } id ? [id] : geoGuessrConfig.Value.Clubs.Select(c => c.ClubId).Distinct();

    /// <summary>Over the view's own period, like everything else the view shows.</summary>
    private async Task<MissionCompletionTimes?> ComputeMissionCompletionAsync(
        string userId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        if (!claimTrackingConfig.Value.Enabled)
        {
            return null;
        }

        var claims = await missionClaims
            .ReadClaimsOfUserAsync(userId, MissionCompletionStats.ReadFrom(from), to, cancellationToken)
            .ConfigureAwait(false);

        var times = MissionCompletionStats.Compute(claims, from, to);
        return times.IsEmpty ? null : times;
    }

    private async Task<(int? ThisWeek, int? LastWeek)> CountHelpsAsync(
        Guid clubId, string userId, CancellationToken cancellationToken)
    {
        if (!viewsConfig.Value.ShowHelps)
        {
            return (null, null);
        }

        var current = await boardReader.ReadCurrentAsync(clubId, cancellationToken).ConfigureAwait(false);
        var previous = await boardReader.ReadPreviousAsync(clubId, cancellationToken).ConfigureAwait(false);

        return (current?.HelpedBy(userId).Count, previous?.HelpedBy(userId).Count);
    }

    [LoggerMessage(LogLevel.Error, "Club with id {clubId} not found.")]
    static partial void LogClubNotFound(ILogger<ActivityReadHandlers> logger, Guid clubId);
}
