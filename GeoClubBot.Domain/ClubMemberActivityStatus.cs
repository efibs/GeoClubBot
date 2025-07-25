namespace Entities;

public record ClubMemberActivityStatus(
    string Nickname,
    bool TargetAchieved,
    bool Excused,
    int XpSinceLastUpdate,
    int NumStrikes,
    bool IsOutOfStrikes);