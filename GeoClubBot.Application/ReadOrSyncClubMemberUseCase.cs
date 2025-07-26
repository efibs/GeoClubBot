using Constants;
using Entities;
using Microsoft.Extensions.Configuration;
using UseCases.InputPorts;
using UseCases.OutputPorts;
using UseCases.OutputPorts.GeoGuessr;

namespace UseCases;

public class ReadOrSyncClubMemberUseCase(
    IClubMemberRepository clubMemberRepository,
    IGeoGuessrAccess geoGuessrAccess,
    IConfiguration config) : IReadOrSyncClubMemberUseCase
{
    public async Task<ClubMember?> ReadOrSyncClubMemberByNicknameAsync(string nickname)
    {
        return await _readOrSyncGenericAsync(nickname, clubMemberRepository.ReadClubMemberByNicknameAsync,
            m => m.User.Nick == nickname);
    }

    public async Task<ClubMember?> ReadOrSyncClubMemberByUserIdAsync(string userId)
    {
        return await _readOrSyncGenericAsync(userId, clubMemberRepository.ReadClubMemberByUserIdAsync,
            m => m.User.UserId == userId);
    }

    private async Task<ClubMember?> _readOrSyncGenericAsync<T>(T id,
        Func<T, Task<ClubMember?>> clubMemberRepositoryRetriever,
        Func<GeoGuessrClubMemberDTO, bool> clubMemberListFinderPredicate)
    {
        // Try to read the club member from the repository
        var clubMember = await clubMemberRepositoryRetriever(id);

        // If the club member was found
        if (clubMember != null)
        {
            return clubMember;
        }

        // Read all the club members of the club
        var geoGuessrClubMembers = await geoGuessrAccess.ReadClubMembersAsync(_clubId);

        // Try to find the club member with the nickname
        var geoGuessrClubMember = geoGuessrClubMembers.FirstOrDefault(clubMemberListFinderPredicate);

        // If the club member could not be found
        if (geoGuessrClubMember == null)
        {
            return null;
        }

        // Build the club member entity
        clubMember = new ClubMember
        {
            UserId = geoGuessrClubMember.User.UserId,
            ClubId = _clubId,
            Nickname = geoGuessrClubMember.User.Nick
        };

        // Save to the database
        var createdClubMember = await clubMemberRepository.CreateClubMemberAsync(clubMember);

        return createdClubMember;
    }

    private readonly Guid _clubId = config.GetValue<Guid>(ConfigKeys.GeoGuessrClubIdConfigurationKey);
}