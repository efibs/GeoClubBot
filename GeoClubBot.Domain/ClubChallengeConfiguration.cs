namespace Entities;

public record ClubChallengeConfigurationDifficulty(
    string Difficulty,
    int RolePriority,
    List<ClubChallengeConfigurationDifficultyEntry> Entries);

public record ClubChallengeConfigurationDifficultyEntry(
    string Description,
    string MapId,
    bool ForbidMoving,
    bool ForbidRotating,
    bool ForbidZooming,
    int TimeLimit);