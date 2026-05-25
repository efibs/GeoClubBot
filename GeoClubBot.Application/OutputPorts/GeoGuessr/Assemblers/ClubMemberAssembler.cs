using Entities;

namespace UseCases.OutputPorts.GeoGuessr.Assemblers;

internal static class ClubMemberAssembler
{
    public static List<ClubMember> AssembleEntities(ICollection<ClubMemberDto> dtos, Guid clubId)
    {
        return dtos.Select(dto => AssembleEntity(dto, clubId)).ToList();
    }

    public static ClubMember AssembleEntity(ClubMemberDto dto, Guid clubId)
    {
        var user = UserAssembler.AssembleEntity(dto.User);
        return ClubMember.Create(user, clubId, dto.Xp, dto.JoinedAt);
    }
}
