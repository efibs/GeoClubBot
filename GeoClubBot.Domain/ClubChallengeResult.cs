namespace Entities;

public record ClubChallengeResult(string Difficulty, int RolePriority, List<ClubChallengeResultPlayer> Players);

public record ClubChallengeResultPlayer(string UserId, string Nickname, string TotalScore, string TotalDistance);