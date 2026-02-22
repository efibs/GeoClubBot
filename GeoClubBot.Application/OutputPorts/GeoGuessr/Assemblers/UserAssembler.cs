using Entities;

namespace UseCases.OutputPorts.GeoGuessr.Assemblers;

internal static class UserAssembler
{
    public static GeoGuessrUser AssembleEntity(ClubMemberUserDto dto)
    {
        return new GeoGuessrUser
        {
            UserId = dto.UserId,
            Nickname = dto.Nick,
            DiscordUserId = null
        };
    }

    public static GeoGuessrUser AssembleEntity(UserDto dto)
    {
        return new GeoGuessrUser
        {
            UserId = dto.Id,
            Nickname = dto.Nick,
            DiscordUserId = null,
        };
    }
}