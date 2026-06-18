namespace GeoClubBot.DTOs;

/// <summary>Request body for the OAuth2 code → token exchange.</summary>
public record ActivityTokenRequest(string Code);

/// <summary>The Discord access token handed back to the Activity frontend.</summary>
public record ActivityTokenResponse(string AccessToken);

/// <summary>
/// Runtime configuration the activity frontend fetches at boot. The Discord application (client) id
/// is public — it's safe to expose — and serving it from server-side config (rather than inlining it
/// at build time) keeps the shipped image generic: each operator points it at their own Discord
/// application via <c>DiscordActivity:ClientId</c> without rebuilding.
/// </summary>
public record ActivityConfigDto(string ClientId);

/// <summary>
/// Aggregate payload powering the Club Dashboard Activity — one fetch per refresh. <see cref="Club"/>
/// is null when the viewer can't be tied to a club (unlinked, or not currently a member); in that
/// case all the panels are empty and the frontend shows a "no club" state.
/// </summary>
public record DashboardDto(
    ClubDto? Club,
    ViewerDto? Viewer,
    IReadOnlyList<LeaderboardEntryDto> Leaderboard,
    IReadOnlyList<ChallengeResultDto> Challenges,
    IReadOnlyList<MissionStreakDto> Streaks);

/// <summary>The viewing member, resolved from their linked Discord account (null when unlinked).</summary>
public record ViewerDto(string Nickname);

public record LeaderboardEntryDto(int Rank, string Nickname, double AverageXp);

public record ChallengeResultDto(string Difficulty, IReadOnlyList<ChallengePlayerDto> Players);

public record ChallengePlayerDto(int Rank, string Nickname, string TotalScore, string TotalDistance);

public record MissionStreakDto(string Nickname, int CurrentStreak, int LongestStreak);
