namespace Entities;

public record ClubChallengeResult(string Difficulty, List<ClubChallengeResultPlayer> Players);

public record ClubChallengeResultPlayer(string Nickname, string TotalScore, string TotalDistance);