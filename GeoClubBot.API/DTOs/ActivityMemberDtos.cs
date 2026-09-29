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

/// <summary>
/// A member's club activity over a period (self-view or admin view): the daily streak, board
/// missions, and — for the current check period only — rule XP and requirement progress.
/// <see cref="HelpedThisWeek"/>/<see cref="HelpedLastWeek"/> count missions of others the member
/// pressed "help out" on; that proves nothing, so they are informational only.
/// </summary>
public record WeekActivityDto(
    int TotalXp,
    int? RuleXp,
    int NumChallengeDaysDone,
    int BoardMissions,
    int BoardClearBonusXp,
    bool JoinedInPeriod,
    DateTimeOffset JoinedAt,
    DateTimeOffset? PeriodStart,
    IReadOnlyList<DayActivityDto> Days,
    IReadOnlyList<RequirementDto> Requirements,
    int? HelpedThisWeek,
    int? HelpedLastWeek);

public record DayActivityDto(DateOnly Date, bool ChallengeDone, int BoardMissions, int Xp);

/// <summary>One weekly requirement, e.g. <c>streak 4/6</c>. <see cref="Label"/> is lower-case and short.</summary>
public record RequirementDto(string Label, int Actual, int Target, bool Met);

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

/// <summary>
/// The running mission board week of the viewer's club. Claimer nicknames are included: the
/// board is visible to every club member in GeoGuessr itself.
/// </summary>
/// <param name="ClaimCycleEnd">When the daily claim allowance resets next.</param>
public record MissionBoardDto(
    string ClubName,
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    int CurrentBoardNumber,
    bool AllBoardsCleared,
    DateTimeOffset ClaimCycleEnd,
    IReadOnlyList<BoardDto> Boards,
    BoardViewerDto? Viewer);

public record BoardDto(int Number, int Size, int CompletedCount, DateTimeOffset? ClearedAt, IReadOnlyList<BoardTileDto> Tiles);

/// <param name="State"><c>free</c>, <c>claimed</c> or <c>completed</c>.</param>
public record BoardTileDto(
    string MissionId,
    string Title,
    int CurrentProgress,
    int TargetProgress,
    string State,
    string? ClaimerNickname,
    DateTimeOffset? ClaimedAt,
    bool HelpRequested,
    int HelperCount,
    bool ClaimedByViewer,
    bool HelpedByViewer);

/// <param name="ClaimState">
/// <c>ClaimAvailable</c>, <c>HoldingOpenMission</c>, <c>ClaimedThisCycle</c> or <c>NoneFree</c>.
/// </param>
public record BoardViewerDto(string ClaimState, int ClaimsThisWeek, int CompletedThisWeek, int HelpedThisWeek);

/// <summary>
/// Today's (UTC) accumulated XP of the viewer's club (null when nothing is tracked yet), how many
/// members kept their streak, how many board missions were finished, and how many members claimed
/// a mission in the current claim cycle (null when the board can't be read).
/// </summary>
public record TodaysXpDto(
    int? Xp,
    string? ClubName,
    int? ChallengeMemberCount,
    int? BoardMissionCount,
    int? ClaimMemberCount,
    int? TotalMemberCount);

/// <summary>
/// One of the viewer's daily reminders. <see cref="Id"/> identifies it for removal;
/// <see cref="TimeUtc"/> is the stored UTC "HH:mm"; <see cref="LocalTime"/> is that time rendered in
/// <see cref="TimeZoneId"/> (or UTC when it's null), matching the <c>/daily-reminder list</c> command.
/// </summary>
public record ReminderDto(string Id, string TimeUtc, string LocalTime, string? TimeZoneId, string? CustomMessage);

public record AddReminderRequest(string LocalTime, string? TimeZoneId, string? CustomMessage);

/// <summary>
/// Result of adding a reminder. The reminder is always persisted; <see cref="DmDelivered"/>
/// reports whether the confirmation DM reached the user (false usually means DMs are disabled).
/// </summary>
public record AddReminderResultDto(ReminderDto Reminder, bool DmDelivered, string? DmErrorCode);

public record StartLinkRequest(string GeoGuessrUserId);
