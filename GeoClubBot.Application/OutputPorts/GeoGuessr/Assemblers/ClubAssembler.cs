using Entities;

namespace UseCases.OutputPorts.GeoGuessr.Assemblers;

internal static class ClubAssembler
{
    public static Club AssembleEntity(ClubDto dto)
    {
        return Club.Create(dto.ClubId, dto.Name, dto.Level);
    }
}
