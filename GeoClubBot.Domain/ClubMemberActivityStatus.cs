namespace Entities;

public record ClubMemberActivityStatus(
    string Nickname,
    bool TargetAchieved,
    int XpSinceLastUpdate,
    int NumStrikes,
    bool IsOutOfStrikes,
    int individualTarget,
    string? individualTargetReason);