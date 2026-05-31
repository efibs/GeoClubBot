using Configuration;
using Entities;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.OutputPorts.Repositories;
using UseCases.OutputPorts.GeoGuessr;

namespace UseCases.UseCases.ClubMemberActivity;

public sealed partial class ActivityReadHandlers(
    IClubRepository clubs,
    IClubMemberRepository clubMembers,
    IGeoGuessrActivityReader activityReader,
    IOptions<GeoGuessrConfiguration> geoGuessrConfig,
    IOptions<DailyMissionReminderConfiguration> missionConfig,
    ILogger<ActivityReadHandlers> logger)
    : IRequestHandler<GetLastCheckTimeQuery, DateTimeOffset?>,
      IRequestHandler<GetActivityThisWeekQuery, ClubMemberWeekActivity>,
      IRequestHandler<GetActivityLastDaysQuery, ClubMemberWeekActivity>
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

    public Task<ClubMemberWeekActivity> Handle(GetActivityThisWeekQuery request, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var startOfWeek = GetStartOfWeek(today);
        return ComputeActivityAsync(request.UserId, startOfWeek, today, cancellationToken);
    }

    public Task<ClubMemberWeekActivity> Handle(GetActivityLastDaysQuery request, CancellationToken cancellationToken)
    {
        var daysBack = Math.Clamp(request.DaysBack, 1, 14);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var startDate = today.AddDays(-(daysBack - 1));
        return ComputeActivityAsync(request.UserId, startDate, today, cancellationToken);
    }

    private async Task<ClubMemberWeekActivity> ComputeActivityAsync(
        string userId,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken)
    {
        var startUtc = new DateTimeOffset(startDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        var daySlots = Enumerable.Range(0, endDate.DayNumber - startDate.DayNumber + 1)
            .Select(i => startDate.AddDays(i))
            .ToList();

        var clubMember = await clubMembers
            .ReadClubMemberByUserIdAsync(userId, cancellationToken)
            .ConfigureAwait(false);

        if (clubMember?.ClubId is null)
        {
            return new ClubMemberWeekActivity(
                TotalXp: 0,
                DailyMissions: daySlots.Select(d => new DayMissionStatus(d, false)).ToList(),
                JoinedThisWeek: false,
                JoinedDateTime: DateTimeOffset.UtcNow);
        }

        var activities = await activityReader
            .ReadActivitiesSinceAsync(clubMember.ClubId.Value, startUtc, cancellationToken)
            .ConfigureAwait(false);

        var memberActivities = activities.Where(a => a.UserId == userId).ToList();

        var dailyMissionXpReward = missionConfig.Value.DailyMissionXpReward;
        var completedDays = memberActivities
            .Where(a => a.XpReward == dailyMissionXpReward)
            .Select(a => DateOnly.FromDateTime(a.RecordedAt.UtcDateTime))
            .ToHashSet();

        var dailyMissions = daySlots
            .Select(d => new DayMissionStatus(d, completedDays.Contains(d)))
            .ToList();

        var totalXp = memberActivities.Sum(a => a.XpReward);
        var joinedInPeriod = clubMember.JoinedAt >= startUtc;

        return new ClubMemberWeekActivity(
            TotalXp: totalXp,
            DailyMissions: dailyMissions,
            JoinedThisWeek: joinedInPeriod,
            JoinedDateTime: clubMember.JoinedAt);
    }

    private static DateOnly GetStartOfWeek(DateOnly date)
    {
        var daysFromMonday = ((int)date.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        return date.AddDays(-daysFromMonday);
    }

    [LoggerMessage(LogLevel.Error, "Club with id {clubId} not found.")]
    static partial void LogClubNotFound(ILogger<ActivityReadHandlers> logger, Guid clubId);
}
