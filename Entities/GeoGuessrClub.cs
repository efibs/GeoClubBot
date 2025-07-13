namespace Entities;

public record GeoGuessrClub(
    string ClubId,
    List<string> Labels,
    string Name,
    int JoinRule,
    string Tag,
    string Description,
    DateTimeOffset CreatedAt,
    string Language,
    int MemberCount,
    int MaxMemberCount,
    GeoGuessrClubLogo Logo,
    int Xp,
    int Level);