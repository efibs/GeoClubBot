namespace Entities;

public record GeoGuessrClubMemberDTO(
    GeoGuessrUserDTO User,
    int Xp,
    DateTimeOffset JoinedAt);