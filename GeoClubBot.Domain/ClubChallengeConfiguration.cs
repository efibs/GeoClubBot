namespace Entities;

public record ClubChallengeConfigurationDifficulty(string Difficulty)
{
    public required int RolePriority { get; init; }

    public required List<ClubChallengeConfigurationDifficultyEntry> Entries { get; init; }
}

public record ClubChallengeConfigurationDifficultyEntry(
    string Description,
    string MapId,
    bool ForbidMoving,
    bool ForbidRotating,
    bool ForbidZooming,
    int TimeLimit);