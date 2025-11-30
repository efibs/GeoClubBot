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
        return new ClubMember
        {
            UserId = dto.User.UserId,
            ClubId = clubId,
            User = UserAssembler.AssembleEntity(dto.User),
            IsCurrentlyMember = true,
            Xp = dto.Xp,
            JoinedAt =  dto.JoinedAt,
            PrivateTextChannelId = null
        };
    }
}