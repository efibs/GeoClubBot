using Configuration;
using Entities;
using Microsoft.Extensions.Options;
using UseCases.InputPorts.ClubMembers;
using UseCases.OutputPorts;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.OutputPorts.GeoGuessr.Assemblers;

namespace UseCases.UseCases.ClubMembers;

public class ReadOrSyncClubMemberUseCase(
    IUnitOfWork unitOfWork,
    IGeoGuessrClientFactory geoGuessrClientFactory,
    ISaveClubMembersUseCase saveClubMembersUseCase,
    IOptions<GeoGuessrConfiguration> geoGuessrConfig) : IReadOrSyncClubMemberUseCase
{
    public async Task<ClubMember?> ReadOrSyncClubMemberByNicknameAsync(string nickname)
    {
        return await _readOrSyncGenericAsync(nickname, unitOfWork.ClubMembers.ReadClubMemberByNicknameAsync,
            m => m.User!.Nickname == nickname).ConfigureAwait(false);
    }

    public async Task<ClubMember?> ReadOrSyncClubMemberByUserIdAsync(string userId)
    {
        return await _readOrSyncGenericAsync(userId, unitOfWork.ClubMembers.ReadClubMemberByUserIdAsync,
            m => m.User!.UserId == userId).ConfigureAwait(false);
    }

    private async Task<ClubMember?> _readOrSyncGenericAsync<T>(T id,
        Func<T, Task<ClubMember?>> clubMemberRepositoryRetriever,
        Func<ClubMember, bool> clubMemberListFinderPredicate)
    {
        // Try to read the club member from the repository
        var clubMember = await clubMemberRepositoryRetriever(id).ConfigureAwait(false);

        // If the club member was found
        if (clubMember != null)
        {
            return clubMember;
        }

        // Iterate all clubs and try to find the member
        foreach (var club in geoGuessrConfig.Value.Clubs)
        {
            // Get the client for this club
            var client = geoGuessrClientFactory.CreateClient(club.ClubId);

            // Read all the club members of the club
            var response = await client.ReadClubMembersAsync(club.ClubId).ConfigureAwait(false);

            // Assemble the entities
            var geoGuessrClubMembers = ClubMemberAssembler.AssembleEntities(response, club.ClubId);

            // Try to find the club member
            var geoGuessrClubMember = geoGuessrClubMembers.FirstOrDefault(clubMemberListFinderPredicate);

            // If the club member was found
            if (geoGuessrClubMember != null)
            {
                // Save the club member
                await saveClubMembersUseCase.SaveClubMembersAsync(Enumerable.Repeat(geoGuessrClubMember, 1)).ConfigureAwait(false);

                return geoGuessrClubMember;
            }
        }

        return null;
    }
}
