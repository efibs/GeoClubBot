namespace Entities;

public record GeoGuessrClubMemberActivityStatus(
    string Nickname,
    bool TargetAchieved,
    int XpSinceLastUpdate,
    int NumStrikes,
    bool IsOutOfStrikes);