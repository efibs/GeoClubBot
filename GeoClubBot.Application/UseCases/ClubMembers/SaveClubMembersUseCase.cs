using Entities;
using Microsoft.Extensions.Configuration;
using UseCases.InputPorts.ClubMembers;
using UseCases.InputPorts.Organization;
using UseCases.InputPorts.Users;
using UseCases.OutputPorts;

namespace UseCases.UseCases.ClubMembers;

public class SaveClubMembersUseCase(ICreateOrUpdateClubMemberUseCase createOrUpdateClubMemberUseCase, 
    IGeoGuessrUserRepository geoGuessrUserRepository, 
    ICreateOrUpdateUserUseCase createOrUpdateUserUseCase) : ISaveClubMembersUseCase
{
    public async Task SaveClubMembersAsync(IEnumerable<ClubMember> clubMembers)
    {
        // For every member of the club
        foreach (var geoGuessrClubMember in clubMembers)
        {
            // Try to read the user
            var geoGuessrUser = await geoGuessrUserRepository.ReadUserByUserIdAsync(geoGuessrClubMember.User!.UserId).ConfigureAwait(false);
            
            // Create the GeoGuessr user entity if the user was not found
            geoGuessrUser ??= new GeoGuessrUser
            {
                UserId = geoGuessrClubMember.User.UserId,
                Nickname = geoGuessrClubMember.User.Nickname
            };
            
            // Update the properties
            geoGuessrUser.Nickname = geoGuessrClubMember.User.Nickname;
            
            // Save the user to the database
            await createOrUpdateUserUseCase.CreateOrUpdateUserAsync(geoGuessrUser).ConfigureAwait(false);
            
            // Ensure the member exists and is up to date
            await createOrUpdateClubMemberUseCase.CreateOrUpdateClubMemberAsync(geoGuessrClubMember).ConfigureAwait(false);
        }
    }
}