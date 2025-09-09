using Constants;
using Entities;
using Microsoft.Extensions.Configuration;
using UseCases.InputPorts.Organization;
using UseCases.OutputPorts;
using UseCases.OutputPorts.GeoGuessr.DTOs;

namespace UseCases.UseCases.Organization;

public class SaveClubMembersUseCase(IClubMemberRepository clubMemberRepository, 
    IGeoGuessrUserRepository geoGuessrUserRepository, 
    IConfiguration config) : ISaveClubMembersUseCase
{
    public async Task SaveClubMembersAsync(IEnumerable<GeoGuessrClubMemberDTO> clubMembers)
    {
        // For every member of the club
        foreach (var geoGuessrClubMember in clubMembers)
        {
            // Try to read the user
            var geoGuessrUser = await geoGuessrUserRepository.ReadUserByUserIdAsync(geoGuessrClubMember.User.Id);
            
            // Create the GeoGuessr user entity if the user was not found
            geoGuessrUser ??= new GeoGuessrUser
            {
                UserId = geoGuessrClubMember.User.Id,
            };
            
            // Update the properties
            geoGuessrUser.Nickname = geoGuessrClubMember.User.Nick;
            
            // Save the user to the database
            await geoGuessrUserRepository.CreateOrUpdateUserAsync(geoGuessrUser);
            
            // Create the member entity
            var member = new ClubMember
            {
                UserId = geoGuessrClubMember.User.Id,
                ClubId = _clubId
            };
            
            // Ensure the member exists and is up to date
            await clubMemberRepository.CreateOrUpdateClubMemberAsync(member);
        }
    }
    
    private readonly Guid _clubId = config.GetValue<Guid>(ConfigKeys.GeoGuessrClubIdConfigurationKey);
}