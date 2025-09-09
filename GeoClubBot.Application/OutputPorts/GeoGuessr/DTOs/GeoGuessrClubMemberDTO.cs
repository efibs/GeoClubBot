using Entities;

namespace UseCases.OutputPorts.GeoGuessr.DTOs;

public record GeoGuessrClubMemberDTO(
    GeoGuessrClubMemberUserDTO User,
    int Xp,
    DateTimeOffset JoinedAt);