namespace UseCases.OutputPorts.GeoGuessr.DTOs;

public record GeoGuessrClubDTO(
    Guid ClubId,
    string Name,
    int Level);