using System.Globalization;
using Entities;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.UseCases.MissionBoard;

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

    public static WeekActivityDto AssembleWeekActivity(ClubMemberActivitySummary activity) => new(
        activity.TotalXp,
        activity.RuleXp,
        activity.NumChallengeDaysDone,
        activity.BoardMissions,
        activity.BoardClearBonusXp,
        activity.JoinedInPeriod,
        activity.JoinedDateTime,
        activity.PeriodStart,
        activity.Days
            .Select(day => new DayActivityDto(day.Date, day.ChallengeDone, day.BoardMissions, day.Xp))
            .ToList(),
        activity.RequirementResults
            .Select(r => new RequirementDto(r.Label, r.Actual, r.Target, r.Met))
            .ToList(),
        activity.HelpedThisWeek,
        activity.HelpedLastWeek);

    public static MissionBoardDto AssembleMissionBoard(ClubMissionBoardView view, string? viewerUserId)
    {
        var board = view.Board;

        var boards = board.Boards
            .Select(b => new BoardDto(
                b.Number,
                b.Size,
                b.CompletedCount,
                b.ClearedAt,
                b.Tiles
                    .Select(t => new BoardTileDto(
                        t.MissionId.ToString(),
                        t.Title,
                        t.CurrentProgress,
                        t.TargetProgress,
                        t.Completed ? "completed" : t.ClaimedBy is null ? "free" : "claimed",
                        t.ClaimedBy is null ? null : view.NicknameOf(t.ClaimedBy),
                        t.ClaimedAt,
                        t.HelpRequested,
                        t.Helpers.Count,
                        viewerUserId is not null && t.ClaimedBy == viewerUserId,
                        viewerUserId is not null && t.Helpers.Contains(viewerUserId)))
                    .ToList()))
            .ToList();

        BoardViewerDto? viewer = null;
        if (viewerUserId is not null)
        {
            var claims = board.ClaimsBy(viewerUserId);
            viewer = new BoardViewerDto(
                board.ClaimStateOf(viewerUserId, view.ClaimCycleStart).ToString(),
                claims.Count,
                claims.Count(t => t.Completed),
                board.HelpedBy(viewerUserId).Count);
        }

        return new MissionBoardDto(
            view.ClubName,
            board.PeriodStart,
            board.PeriodEnd,
            board.CurrentBoardNumber,
            board.AllBoardsCleared,
            view.ClaimCycleStart.AddDays(1),
            boards,
            viewer);
    }

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
}
