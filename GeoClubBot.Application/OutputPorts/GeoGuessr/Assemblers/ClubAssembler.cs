using Entities;

namespace UseCases.OutputPorts.GeoGuessr.Assemblers;

internal static class ClubAssembler
{
    public static Club AssembleEntity(ClubDto dto)
    {
        return new Club
        {
            ClubId = dto.ClubId,
            Name = dto.Name,
            Level = dto.Level,
            LatestActivityCheckTime = null,
        };
    }
}