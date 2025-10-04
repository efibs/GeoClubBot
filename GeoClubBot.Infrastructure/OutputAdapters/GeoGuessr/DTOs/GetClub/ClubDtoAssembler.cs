using Entities;

namespace Infrastructure.OutputAdapters.GeoGuessr.DTOs.GetClub;

public static class ClubDtoAssembler
{
    public static Club AssembleEntity(ClubDto dto)
    {
        return new Club
        {
            ClubId = dto.ClubId,
            Name = dto.Name,
            Level = dto.Level
        };
    }
}