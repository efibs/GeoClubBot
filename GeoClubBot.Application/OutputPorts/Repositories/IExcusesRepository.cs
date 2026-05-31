using Entities;
using UseCases.OutputPorts.Projections;

namespace UseCases.OutputPorts.Repositories;

public interface IExcusesRepository
{
    ClubMemberExcuse CreateExcuse(ClubMemberExcuse excuse);

    Task<List<ClubMemberExcuse>> ReadExcusesAsync(CancellationToken cancellationToken = default);

    Task<List<ExcuseProjection>> ReadExcuseProjectionsAsync(CancellationToken cancellationToken = default);

    Task<ClubMemberExcuse?> ReadExcuseAsync(Guid id, CancellationToken cancellationToken = default);

    Task<ClubMemberExcuse?> ReadForUpdateByIdAsync(Guid excuseId, CancellationToken cancellationToken = default);

    Task<List<ClubMemberExcuse>> ReadExcusesByMemberNicknameAsync(string memberNickname, CancellationToken cancellationToken = default);

    void DeleteExcuse(ClubMemberExcuse excuse);

    Task<int> DeleteExcusesBeforeAsync(DateTimeOffset threshold, CancellationToken cancellationToken = default);
}
