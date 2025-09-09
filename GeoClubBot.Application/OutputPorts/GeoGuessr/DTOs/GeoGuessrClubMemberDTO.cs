namespace UseCases.OutputPorts.GeoGuessr.DTOs;

public record GeoGuessrClubMemberDTO(
    GeoGuessrUserDTO User,
    int Xp,
    DateTimeOffset JoinedAt);