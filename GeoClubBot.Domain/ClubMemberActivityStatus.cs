namespace Entities;

public record ClubMemberActivityStatus(
    string Nickname,
    string UserId,
    bool TargetAchieved,
    int XpSinceLastUpdate,
    int NumStrikes,
    bool IsOutOfStrikes,
    int IndividualTarget,
    string? IndividualTargetReason);