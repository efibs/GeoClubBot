using Entities;

namespace UseCases.InputPorts.ClubMembers;

public interface IReadOrSyncClubMemberUseCase
{
    Task<ClubMember?> ReadOrSyncClubMemberByNicknameAsync(string nickname);
    
    Task<ClubMember?> ReadOrSyncClubMemberByUserIdAsync(string userId);
}