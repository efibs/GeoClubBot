namespace GeoClubBot.DTOs;

/// <summary>Request body for the OAuth2 code → token exchange.</summary>
public record ActivityTokenRequest(string Code);

/// <summary>The Discord access token handed back to the Activity frontend.</summary>
public record ActivityTokenResponse(string AccessToken);

/// <summary>Aggregate payload powering the Club Dashboard Activity — one fetch per refresh.</summary>
public record DashboardDto(
    ClubDto Club,
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
