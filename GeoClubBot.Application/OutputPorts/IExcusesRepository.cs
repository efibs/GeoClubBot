using Entities;

namespace UseCases.OutputPorts;

public interface IExcusesRepository
{
    ClubMemberExcuse CreateExcuse(ClubMemberExcuse excuse);
    
    Task<List<ClubMemberExcuse>> ReadExcusesAsync();

    Task<ClubMemberExcuse?> ReadExcuseAsync(Guid id);
    
    Task<List<ClubMemberExcuse>> ReadExcusesByMemberNicknameAsync(string memberNickname);
    
    Task<ClubMemberExcuse?> UpdateExcuseAsync(Guid excuseId, DateTimeOffset newFrom, DateTimeOffset newTo);
    
    void DeleteExcuse(ClubMemberExcuse excuse);
    
    Task<int> DeleteExcusesBeforeAsync(DateTimeOffset threshold);
}