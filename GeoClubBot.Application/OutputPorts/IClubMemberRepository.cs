using Entities;

namespace UseCases.OutputPorts;

public interface IClubMemberRepository
{
    Task<ClubMember?> CreateClubMemberAsync(ClubMember clubMember);

    Task<ClubMember?> ReadClubMemberByNicknameAsync(string nickname);

    Task<int> DeleteClubMembersWithoutHistoryAsync();
}