using Entities;

namespace UseCases.OutputPorts;

public interface IClubMemberRepository
{
    ClubMember CreateClubMember(ClubMember clubMember);
    
    Task<ClubMember?> UpdateClubMemberAsync(ClubMember clubMember);
    
    Task<ClubMember?> ReadClubMemberByNicknameAsync(string nickname);
    
    Task<ClubMember?> ReadClubMemberByUserIdAsync(string userId);
    
    Task<List<ClubMember>> ReadClubMembersAsync();

    Task<int> DeleteClubMembersWithoutHistoryAndStrikesAsync();
}