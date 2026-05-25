using Entities;

namespace UseCases.OutputPorts;

public interface IClubMemberRepository
{
    void AddClubMember(ClubMember clubMember);

    Task<ClubMember?> ReadClubMemberByNicknameAsync(string nickname);

    Task<ClubMember?> ReadClubMemberByUserIdAsync(string userId);

    Task<ClubMember?> ReadForUpdateByUserIdAsync(string userId);

    Task<List<ClubMember>> ReadClubMembersAsync();

    Task<List<ClubMember>> ReadClubMembersByClubIdAsync(Guid clubId);

    Task<int> DeleteClubMembersWithoutHistoryAndStrikesAsync();
}
