using Entities;

namespace UseCases.OutputPorts;

public interface IExcusesRepository
{
    Task<ClubMemberExcuse?> CreateExcuseAsync(ClubMemberExcuse excuse);
    
    Task<List<ClubMemberExcuse>> ReadExcusesAsync();

    Task<List<ClubMemberExcuse>> ReadExcusesByMemberNicknameAsync(string memberNickname);
    
    Task<bool> DeleteExcuseByIdAsync(Guid excuseId);
    
    Task<int> DeleteExcusesBeforeAsync(DateTimeOffset threshold);
}