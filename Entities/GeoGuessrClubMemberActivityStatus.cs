namespace Entities;

public record GeoGuessrClubMemberActivityStatus(
    string Nickname,
    bool TargetAchieved,
    bool Excused,
    int XpSinceLastUpdate,
    int NumStrikes,
    bool IsOutOfStrikes);