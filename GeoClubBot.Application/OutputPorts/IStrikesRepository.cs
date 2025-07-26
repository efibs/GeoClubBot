using Entities;

namespace UseCases.OutputPorts;

public interface IStrikesRepository
{
    Task<ClubMemberStrike?> CreateStrikeAsync(ClubMemberStrike strike);

    Task<int?> ReadNumberOfActiveStrikesByMemberUserIdAsync(string memberUserId);
    
    Task<int?> ReadNumberOfActiveStrikesByMemberNicknameAsync(string memberNickname);
    
    Task<bool> RevokeStrikeByIdAsync(Guid strikeId);
    
    Task<int> DeleteStrikesBeforeAsync(DateTimeOffset threshold);
}