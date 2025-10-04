namespace Infrastructure.OutputAdapters.GeoGuessr.DTOs.GetClub;

public record ClubDto(
    Guid ClubId,
    string Name,
    int Level);