using Entities;

namespace GeoClubBot.DTOs.Assemblers;

public static class ClubDtoAssembler
{
    public static ClubDto AssembleDto(Club club)
    {
        return new ClubDto(club.Name, club.Level);
    }
}