namespace Entities;

public record GeoGuessrClubMember(GeoGuessrUser User, 
    DateTimeOffset JoinedAt, 
    bool IsOnline, 
    int Xp);