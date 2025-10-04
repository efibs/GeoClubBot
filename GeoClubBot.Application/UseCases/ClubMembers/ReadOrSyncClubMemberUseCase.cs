using Constants;
using Entities;
using Microsoft.Extensions.Configuration;
using UseCases.InputPorts.ClubMembers;
using UseCases.InputPorts.Organization;
using UseCases.OutputPorts;
using UseCases.OutputPorts.GeoGuessr;

namespace UseCases.UseCases.ClubMembers;

public class ReadOrSyncClubMemberUseCase(
    IClubMemberRepository clubMemberRepository,
    IGeoGuessrAccess geoGuessrAccess,
    ISaveClubMembersUseCase saveClubMembersUseCase,
    IConfiguration config) : IReadOrSyncClubMemberUseCase
{
    public async Task<ClubMember?> ReadOrSyncClubMemberByNicknameAsync(string nickname)
    {
        return await _readOrSyncGenericAsync(nickname, clubMemberRepository.ReadClubMemberByNicknameAsync,
            m => m.User!.Nickname == nickname).ConfigureAwait(false);
    }

    public async Task<ClubMember?> ReadOrSyncClubMemberByUserIdAsync(string userId)
    {
        return await _readOrSyncGenericAsync(userId, clubMemberRepository.ReadClubMemberByUserIdAsync,
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

        // Read all the club members of the club
        var geoGuessrClubMembers = await geoGuessrAccess.ReadClubMembersAsync(_clubId).ConfigureAwait(false);

        // Try to find the club member with the nickname
        var geoGuessrClubMember = geoGuessrClubMembers.FirstOrDefault(clubMemberListFinderPredicate);

        // If the club member could not be found
        if (geoGuessrClubMember == null)
        {
            return null;
        }

        // Save the club member
        await saveClubMembersUseCase.SaveClubMembersAsync(Enumerable.Repeat(geoGuessrClubMember, 1)).ConfigureAwait(false);

        return clubMember;
    }

    private readonly Guid _clubId = config.GetValue<Guid>(ConfigKeys.GeoGuessrClubIdConfigurationKey);
}