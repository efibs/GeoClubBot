namespace GeoClubBot.DTOs;

/// <summary>
/// The authenticated viewer's own session context, fetched once by the activity frontend after the
/// OAuth handshake. Drives which tabs are shown (<see cref="IsAdmin"/> is cosmetic there — every
/// admin endpoint re-checks the same policy server-side) and the linked/unlinked UI states.
/// Discord ids are serialized as strings: snowflakes exceed the 2^53 range JS numbers can hold.
/// </summary>
public record MeDto(
    string DiscordUserId,
    bool IsAdmin,
    LinkedAccountDto? Linked,
    ClubDto? Club,
    LinkRequestDto? OpenLinkRequest);

/// <summary>The viewer's linked GeoGuessr account (null in <see cref="MeDto"/> when unlinked).</summary>
public record LinkedAccountDto(string GeoGuessrUserId, string Nickname);

/// <summary>
/// The viewer's own open account-linking request. Carries the one-time password so the frontend can
/// re-display it — the OTP is a secret shared between the requesting member and the admins, so this
/// DTO must only ever be assembled for the request's owner (the authenticated caller). The admin
/// listing uses a separate DTO without the OTP.
/// </summary>
public record LinkRequestDto(string GeoGuessrUserId, string OneTimePassword);

/// <summary>A member's XP + daily-mission activity over a trailing window (self-view or admin view).</summary>
public record WeekActivityDto(
    int TotalXp,
    int NumDaysDone,
    bool JoinedThisWeek,
    DateTimeOffset JoinedAt,
    IReadOnlyList<DayMissionDto> Days);

public record DayMissionDto(DateOnly Date, bool MissionCompleted);

/// <summary>The viewer's GeoGuessr profile, with ranked stats when the player has any.</summary>
public record ProfileDto(
    string Nickname,
    string CountryCode,
    DateTimeOffset CreatedAt,
    bool IsProUser,
    int? Level,
    string ProfileUrl,
    RankedDto? Ranked);

public record RankedDto(int? Rating, string? DivisionName, string? Tier, int? PeakRating);

/// <summary>Aggregated daily-mission statistics for the viewer's club (or across all clubs).</summary>
public record MissionStatsDto(
    string? ClubName,
    DateOnly FromDay,
    DateOnly ToDay,
    int DaysWithMissionData,
    int TotalMissionAppearances,
    double? AverageDayCompletionRate,
    IReadOnlyList<MissionKindStatsDto> Kinds);

public record MissionKindStatsDto(
    string Type,
    string GameMode,
    int AppearanceCount,
    double AppearanceDayShare,
    double AverageTargetProgress,
    DateOnly LastAppearance,
    double? AverageDayCompletionRateWhenPresent);

/// <summary>Today's accumulated XP of the viewer's club (null when nothing is tracked yet).</summary>
public record TodaysXpDto(int? Xp, string? ClubName);

/// <summary>The viewer's daily-mission reminder; times are UTC "HH:mm" (frontend renders local).</summary>
public record ReminderDto(string TimeUtc, string? TimeZoneId, string? CustomMessage);

/// <summary>Wrapper so "no reminder configured" is a plain 200 instead of a 404.</summary>
public record ReminderStatusDto(ReminderDto? Reminder);

public record SetReminderRequest(string LocalTime, string? TimeZoneId, string? CustomMessage);

/// <summary>
/// Result of setting a reminder. The reminder is always persisted; <see cref="DmDelivered"/>
/// reports whether the confirmation DM reached the user (false usually means DMs are disabled).
/// </summary>
public record ReminderUpdateResultDto(ReminderDto Reminder, bool DmDelivered, string? DmErrorCode);

public record StartLinkRequest(string GeoGuessrUserId);
