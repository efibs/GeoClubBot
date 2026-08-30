using System.Globalization;
using Entities;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.UseCases.DailyMissionStatistics;

namespace GeoClubBot.DTOs.Assemblers;

public static class ActivityMemberDtoAssembler
{
    public static ReminderDto AssembleReminder(DailyMissionReminder reminder) => new(
        reminder.Id.ToString(),
        reminder.ReminderTimeUtc.ToString("HH\\:mm", CultureInfo.InvariantCulture),
        ConvertToLocal(reminder.ReminderTimeUtc, reminder.TimeZoneId)
            .ToString("HH\\:mm", CultureInfo.InvariantCulture),
        reminder.TimeZoneId,
        reminder.CustomMessage);

    /// <summary>
    /// Renders a UTC reminder time in its stored IANA time zone for display, resolving DST against
    /// today (mirrors the <c>/daily-reminder list</c> slash command). Falls back to UTC when the
    /// zone is absent or unknown.
    /// </summary>
    private static TimeOnly ConvertToLocal(TimeOnly utcTime, string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            return utcTime;
        }

        try
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var utcDateTime = today.ToDateTime(utcTime, DateTimeKind.Utc);
            var localDateTime = TimeZoneInfo.ConvertTimeFromUtc(utcDateTime, tz);
            return TimeOnly.FromDateTime(localDateTime);
        }
        catch
        {
            return utcTime;
        }
    }

    public static WeekActivityDto AssembleWeekActivity(ClubMemberWeekActivity activity) => new(
        activity.TotalXp,
        activity.NumDaysDone,
        activity.NumMissionDaysDone,
        activity.NumChallengeDaysDone,
        activity.JoinedThisWeek,
        activity.JoinedDateTime,
        activity.DailyMissions
            .Select(day => new DayMissionDto(day.Date, day.MissionCompleted, day.ChallengeCompleted))
            .ToList());

    public static ProfileDto AssembleProfile(
        UserDto profile,
        RankedProgressResponseDto? rankedProgress,
        RankedPeakRatingResponseDto? rankedPeak)
    {
        // Ranked stats are optional: players who never played ranked simply get none.
        RankedDto? ranked = rankedProgress is null && rankedPeak is null
            ? null
            : new RankedDto(
                rankedProgress?.Rating,
                rankedProgress?.DivisionName,
                rankedProgress?.Tier,
                rankedPeak?.PeakOverallRating);

        return new ProfileDto(
            profile.Nick,
            profile.CountryCode,
            profile.Created,
            profile.IsProUser,
            profile.Progress?.Level,
            profile.Url,
            ranked);
    }

    public static MissionStatsDto AssembleMissionStats(DailyMissionStatistics statistics) => new(
        statistics.ClubName,
        statistics.FromDay,
        statistics.ToDay,
        statistics.DaysWithMissionData,
        statistics.TotalMissionAppearances,
        statistics.AverageDayCompletionRate,
        statistics.Kinds
            .Select(kind => new MissionKindStatsDto(
                kind.Type,
                kind.GameMode,
                kind.AppearanceCount,
                kind.AppearanceDayShare,
                kind.AverageTargetProgress,
                kind.LastAppearance,
                kind.AverageDayCompletionRateWhenPresent))
            .ToList(),
        statistics.AverageDayChallengeRate,
        statistics.DaysWithChallengeData,
        statistics.ChallengeTrackedFrom);
}
