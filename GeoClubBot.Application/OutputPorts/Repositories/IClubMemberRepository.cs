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

    Task<List<ClubMember>> ReadClubMembersByClubIdAsync(Guid clubId, CancellationToken cancellationToken = default);

    Task<int> DeleteClubMembersWithoutHistoryAndStrikesAsync(CancellationToken cancellationToken = default);
}
