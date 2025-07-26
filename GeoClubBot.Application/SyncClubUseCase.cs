using Constants;
using Entities;
using Microsoft.Extensions.Configuration;
using UseCases.InputPorts;
using UseCases.OutputPorts;
using UseCases.OutputPorts.GeoGuessr;

namespace UseCases;

public class SyncClubUseCase(
    IGeoGuessrAccess geoGuessrAccess,
    IClubRepository clubRepository,
    IClubMemberRepository clubMemberRepository,
    IConfiguration config) : ISyncClubUseCase
{
    public async Task SyncClubAsync()
    {
        // Read the GeoGuessr club
        var geoGuessrClub = await geoGuessrAccess.ReadClubAsync(_clubId);

        // Create the club entity
        var club = new Club(geoGuessrClub.ClubId, geoGuessrClub.Name, geoGuessrClub.Level);
        
        // Sync the club
        await clubRepository.CreateOrUpdateClubAsync(club);
        
        // Read the members of the club 
        var geoGuessrClubMembers = await geoGuessrAccess.ReadClubMembersAsync(_clubId);
        
        // For every member of the club
        foreach (var geoGuessrClubMember in geoGuessrClubMembers)
        {
            // Create the member entity
            var member = new ClubMember(geoGuessrClubMember.UserDto.UserId, club.ClubId,
                geoGuessrClubMember.UserDto.Nick);
            
            // Sync the member
            await clubMemberRepository.CreateOrUpdateClubMemberAsync(member);
        }
    }

    private readonly Guid _clubId = config.GetValue<Guid>(ConfigKeys.GeoGuessrClubIdConfigurationKey);
}