using Entities;

namespace UseCases.OutputPorts;

public interface IStrikesRepository
{
    Task<ClubMemberStrike?> CreateStrikeAsync(ClubMemberStrike strike);

    Task<int?> ReadNumberOfActiveStrikesByMemberUserIdAsync(string memberUserId);
    
    Task<List<ClubMemberStrike>?> ReadStrikesByMemberNicknameAsync(string memberNickname);
    
    Task<bool> RevokeStrikeByIdAsync(Guid strikeId);
    
    Task<bool> UnrevokeStrikeByIdAsync(Guid strikeId);
    
    Task<int> DeleteStrikesBeforeAsync(DateTimeOffset threshold);
}