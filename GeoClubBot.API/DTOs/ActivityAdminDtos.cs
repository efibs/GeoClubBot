namespace GeoClubBot.DTOs;

// DTOs served exclusively by ActivityAdminController (policy-gated). Keep them separate from the
// member DTOs so no admin-only field can leak into a member payload by reuse.

/// <summary>When the activity checker last ran (null when it hasn't run yet).</summary>
public record LastCheckTimeDto(DateTimeOffset? LastCheckTime);

public record AdminStrikeDto(
    Guid StrikeId,
    string Nickname,
    DateTimeOffset Timestamp,
    bool Revoked,
    DateTimeOffset ExpiresAt);

/// <summary>Members that currently carry at least one active strike.</summary>
public record AdminRelevantStrikeDto(string Nickname, int NumActiveStrikes);

public record AdminMemberStrikesDto(int NumActiveStrikes, IReadOnlyList<AdminStrikeDto> Strikes);

public record AdminExcuseDto(Guid ExcuseId, string Nickname, DateTimeOffset From, DateTimeOffset To);

/// <summary>
/// An open account-linking request as admins see it: WITHOUT the one-time password. The OTP must
/// reach the admin via GeoGuessr DM from the requesting member — that's what proves account
/// ownership — so it is structurally absent here.
/// </summary>
public record AdminLinkRequestDto(string DiscordUserId, string GeoGuessrUserId);

public record PlayerStatisticsDto(
    string Nickname,
    DateTimeOffset HistorySince,
    int NumHistoryEntries,
    double AveragePoints,
    int MinPoints,
    int FirstQuartilePoints,
    int MedianPoints,
    int ThirdQuartilePoints,
    int MaxPoints);

public record ClubStatisticsDto(
    string ClubName,
    double AverageAveragePoints,
    double MinAveragePoints,
    double FirstQuartileAveragePoints,
    double MedianAveragePoints,
    double ThirdQuartileAveragePoints,
    double MaxAveragePoints);

// --- Admin write requests/responses ---

public record AddStrikeRequest(string MemberNickname, DateTimeOffset StrikeDate);

public record StrikeCreatedDto(Guid StrikeId);

public record AddExcuseRequest(string MemberNickname, DateTimeOffset From, DateTimeOffset To);

public record ExcuseCreatedDto(Guid ExcuseId);

public record UpdateExcuseRequest(DateTimeOffset From, DateTimeOffset To);

/// <summary>The admin types the OTP the member sent them via GeoGuessr DM — same as the Discord flow.</summary>
public record CompleteLinkRequestRequest(string DiscordUserId, string GeoGuessrUserId, string OneTimePassword);

public record CancelLinkRequestRequest(string DiscordUserId, string GeoGuessrUserId);

public record UnlinkAccountsRequest(string DiscordUserId, string GeoGuessrUserId);

public record LinkCompletedDto(string GeoGuessrUserId, string Nickname);
