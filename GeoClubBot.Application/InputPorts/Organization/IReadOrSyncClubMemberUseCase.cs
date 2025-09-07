using Entities;

namespace UseCases.InputPorts.Organization;

public interface IReadOrSyncClubMemberUseCase
{
    Task<ClubMember?> ReadOrSyncClubMemberByNicknameAsync(string nickname);
    
    Task<ClubMember?> ReadOrSyncClubMemberByUserIdAsync(string userId);
}