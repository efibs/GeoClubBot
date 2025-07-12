namespace Entities;

public record GeoGuessrClubMemberActivityEntry(Guid UserId, 
    string Nickname, 
    int Xp,
    DateTimeOffset Timestamp);