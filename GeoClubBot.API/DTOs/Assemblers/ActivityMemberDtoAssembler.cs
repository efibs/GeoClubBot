using System.Globalization;
using Entities;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.UseCases.DailyMissionStatistics;

namespace GeoClubBot.DTOs.Assemblers;

public static class ActivityMemberDtoAssembler
{
    public static ReminderDto AssembleReminder(DailyMissionReminder reminder) => new(
        reminder.ReminderTimeUtc.ToString("HH\\:mm", CultureInfo.InvariantCulture),
        reminder.TimeZoneId,
        reminder.CustomMessage);

    public static WeekActivityDto AssembleWeekActivity(ClubMemberWeekActivity activity) => new(
        activity.TotalXp,
        activity.NumDaysDone,
        activity.JoinedThisWeek,
        activity.JoinedDateTime,
        activity.DailyMissions
            .Select(day => new DayMissionDto(day.Date, day.MissionCompleted))
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
            .ToList());
}
