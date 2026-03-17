using Entities;
using UseCases.InputPorts.ClubMembers;
using UseCases.InputPorts.Users;

namespace UseCases.UseCases.ClubMembers;

public class SaveClubMembersUseCase(ICreateOrUpdateClubMemberUseCase createOrUpdateClubMemberUseCase,
    ICreateOrUpdateUserUseCase createOrUpdateUserUseCase) : ISaveClubMembersUseCase
{
    public async Task SaveClubMembersAsync(IEnumerable<ClubMember> clubMembers)
    {
        foreach (var clubMember in clubMembers)
        {
            // Ensure the user exists and is up to date
            await createOrUpdateUserUseCase.CreateOrUpdateUserAsync(clubMember.User).ConfigureAwait(false);

            // Ensure the club member exists and is up to date
            await createOrUpdateClubMemberUseCase.CreateOrUpdateClubMemberAsync(clubMember).ConfigureAwait(false);
        }
    }
}