using Entities;

namespace Infrastructure.OutputAdapters.GeoGuessr.DTOs.GetClubMembers;

public static class ClubMemberDtoAssembler
{
    public static List<ClubMember> AssembleEntities(IEnumerable<ClubMemberDto> dtos, Guid clubId)
    {
        return dtos.Select(dto => AssembleEntity(dto, clubId)).ToList();
    }
    
    public static ClubMember AssembleEntity(ClubMemberDto dto, Guid clubId)
    {
        return new ClubMember
        {
            UserId = dto.User.UserId,
            User = UserDtoAssembler.AssembleEntity(dto.User),
            ClubId = clubId,
            IsCurrentlyMember = true,
            Xp = dto.Xp,
            JoinedAt = dto.JoinedAt
        };
    }
}