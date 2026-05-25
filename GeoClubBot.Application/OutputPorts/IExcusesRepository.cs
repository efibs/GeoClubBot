using Entities;

namespace UseCases.OutputPorts;

public interface IExcusesRepository
{
    ClubMemberExcuse CreateExcuse(ClubMemberExcuse excuse);

    Task<List<ClubMemberExcuse>> ReadExcusesAsync();

    Task<ClubMemberExcuse?> ReadExcuseAsync(Guid id);

    Task<ClubMemberExcuse?> ReadForUpdateByIdAsync(Guid excuseId);

    Task<List<ClubMemberExcuse>> ReadExcusesByMemberNicknameAsync(string memberNickname);

    void DeleteExcuse(ClubMemberExcuse excuse);

    Task<int> DeleteExcusesBeforeAsync(DateTimeOffset threshold);
}
