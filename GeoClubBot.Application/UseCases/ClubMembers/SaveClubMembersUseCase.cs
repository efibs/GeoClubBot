using Entities;
using UseCases.InputPorts.ClubMembers;
using UseCases.InputPorts.Users;
using UseCases.OutputPorts;

namespace UseCases.UseCases.ClubMembers;

public class SaveClubMembersUseCase(ICreateOrUpdateClubMemberUseCase createOrUpdateClubMemberUseCase, 
    IUnitOfWork unitOfWork,
    ICreateOrUpdateUserUseCase createOrUpdateUserUseCase) : ISaveClubMembersUseCase
{
    public async Task SaveClubMembersAsync(IEnumerable<ClubMember> clubMembers)
    {
        // For every member of the club
        foreach (var geoGuessrClubMember in clubMembers)
        {
            // Try to read the user
            var geoGuessrUser = await unitOfWork.GeoGuessrUsers.ReadUserByUserIdAsync(geoGuessrClubMember.User!.UserId).ConfigureAwait(false);
            
            // Create the GeoGuessr user entity if the user was not found
            geoGuessrUser ??= new GeoGuessrUser
            {
                UserId = geoGuessrClubMember.User.UserId,
                Nickname = geoGuessrClubMember.User.Nickname
            };
            
            // Update the properties
            geoGuessrUser.Nickname = geoGuessrClubMember.User.Nickname;
            
            // Save the user to the database
            var trackedUser = await createOrUpdateUserUseCase.CreateOrUpdateUserAsync(geoGuessrUser).ConfigureAwait(false);

            // Try to read the club member from the database
            var clubMemberFromDatabase = await unitOfWork.ClubMembers
                .ReadClubMemberByUserIdAsync(geoGuessrClubMember.User.UserId)
                .ConfigureAwait(false);

            // Set if not found
            var updatedClubMember = clubMemberFromDatabase ?? geoGuessrClubMember;

            // Ensure the navigation property references the tracked user instance
            // to avoid EF tracking conflicts when adding a new club member
            updatedClubMember.User = trackedUser;
            
            // Update the properties
            updatedClubMember.IsCurrentlyMember = geoGuessrClubMember.IsCurrentlyMember;
            updatedClubMember.JoinedAt = geoGuessrClubMember.JoinedAt;
            updatedClubMember.Xp = geoGuessrClubMember.Xp;
            
            // Ensure the member exists and is up to date
            await createOrUpdateClubMemberUseCase.CreateOrUpdateClubMemberAsync(updatedClubMember).ConfigureAwait(false);
        }
    }
}