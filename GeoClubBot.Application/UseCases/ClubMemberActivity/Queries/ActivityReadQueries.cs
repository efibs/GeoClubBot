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
    ClubActivityKindClassifier activityKinds,
    IOptions<GeoGuessrConfiguration> geoGuessrConfig,
    IOptions<ActivityCheckerConfiguration> activityCheckerConfig,
    IOptions<ActivityViewsConfiguration> viewsConfig,
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

        if (clubMember?.ClubId is not { } clubId)
        {
            return new ClubMemberActivitySummary(
                Days: daySlots.Select(d => new DayActivity(d, false, 0, 0)).ToList(),
                TotalXp: 0,
                StreakDays: 0,
                BoardMissions: 0,
                BoardClearBonusXp: 0,
                JoinedInPeriod: false,
                JoinedDateTime: DateTimeOffset.UtcNow,
                PeriodStart: from);
        }

        var activities = await activityReader
            .ReadActivitiesSinceAsync(clubId, from, cancellationToken)
            .ConfigureAwait(false);

        var memberActivities = activities
            .Where(a => a.UserId == userId && a.RecordedAt >= from && a.RecordedAt <= to)
            .ToList();

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
        if (isCheckPeriod && geoGuessrConfig.Value.Clubs.Any(c => c.ClubId == clubId))
        {
            var rules = ActivityRules.Resolve(activityCheckerConfig.Value, geoGuessrConfig.Value.GetClub(clubId));
            var evaluation = ActivityRuleEvaluator.Evaluate(
                ClubActivityEntries.From(memberActivities, activityKinds), rules, targetFactor: 1);
            ruleXp = evaluation.RuleXp;
            requirements = evaluation.Requirements;
        }

        var (helpedThisWeek, helpedLastWeek) = await CountHelpsAsync(clubId, userId, cancellationToken).ConfigureAwait(false);

        return new ClubMemberActivitySummary(
            Days: days,
            TotalXp: memberActivities.Sum(a => a.XpReward),
            StreakDays: memberActivities.Count(activityKinds.IsDailyChallenge),
            BoardMissions: memberActivities.Count(activityKinds.IsBoardMission),
            BoardClearBonusXp: memberActivities.Where(activityKinds.IsBoardClearBonus).Sum(a => a.XpReward),
            JoinedInPeriod: clubMember.JoinedAt >= from,
            JoinedDateTime: clubMember.JoinedAt,
            RuleXp: ruleXp,
            Requirements: requirements,
            HelpedThisWeek: helpedThisWeek,
            HelpedLastWeek: helpedLastWeek,
            PeriodStart: from);
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
