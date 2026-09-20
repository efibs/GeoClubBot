using Entities;

namespace UseCases.OutputPorts.Repositories;

public interface IClubMemberRepository
{
    void AddClubMember(ClubMember clubMember);

    Task<ClubMember?> ReadClubMemberByNicknameAsync(string nickname, CancellationToken cancellationToken = default);

    Task<ClubMember?> ReadClubMemberByUserIdAsync(string userId, CancellationToken cancellationToken = default);

    Task<Dictionary<string, ClubMember>> ReadClubMembersByUserIdsAsync(IReadOnlyCollection<string> userIds, CancellationToken cancellationToken = default);

    Task<ClubMember?> ReadForUpdateByUserIdAsync(string userId, CancellationToken cancellationToken = default);

    Task<List<ClubMember>> ReadClubMembersAsync(CancellationToken cancellationToken = default);

    Task<List<string>> ReadAllNicknamesAsync(CancellationToken cancellationToken = default);

    Task<List<ClubMember>> ReadClubMembersByClubIdAsync(Guid clubId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Members whose private channel has been archived since before <paramref name="threshold"/> and
    /// who are still in no club, i.e. whose channel is due to be deleted for good.
    /// </summary>
    Task<List<ClubMember>> ReadMembersWithExpiredArchivedPrivateChannelsAsync(DateTimeOffset threshold,
        CancellationToken cancellationToken = default);

    Task<int> DeleteClubMembersWithoutHistoryAndStrikesAsync(CancellationToken cancellationToken = default);
}
