using Entities;

namespace Infrastructure.OutputAdapters.GeoGuessr.DTOs.GetClubMembers;

public static class UserDtoAssembler
{
    public static GeoGuessrUser AssembleEntity(UserDto dto)
    {
        return new GeoGuessrUser
        {
            UserId = dto.UserId,
            Nickname = dto.Nick,
            DiscordUserId = null
        };
    }
}