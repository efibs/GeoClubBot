using Entities;

namespace UseCases.InputPorts;

public interface IReadOrSyncClubMemberUseCase
{
    Task<ClubMember?> ReadOrSyncClubMemberByNicknameAsync(string nickname);
}