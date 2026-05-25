using Entities;

namespace UseCases.OutputPorts.GeoGuessr.Assemblers;

internal static class UserAssembler
{
    public static GeoGuessrUser AssembleEntity(ClubMemberUserDto dto)
    {
        return GeoGuessrUser.Create(dto.UserId, dto.Nick);
    }

    public static GeoGuessrUser AssembleEntity(UserDto dto)
    {
        return GeoGuessrUser.Create(dto.Id, dto.Nick);
    }
}
