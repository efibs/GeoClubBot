using Entities;

namespace Infrastructure.OutputAdapters.GeoGuessr.DTOs.GetUser;

public static class UserDtoAssembler
{
    public static GeoGuessrUser AssembleEntity(UserDto dto)
    {
        return new GeoGuessrUser
        {
            UserId = dto.Id,
            Nickname = dto.Nick
        };
    }
}