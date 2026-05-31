using Entities;

namespace UseCases.OutputPorts.Repositories;

public interface IStrikesRepository
{
    ClubMemberStrike CreateStrike(ClubMemberStrike strike);

    Task<int?> ReadNumberOfActiveStrikesByMemberUserIdAsync(string memberUserId, CancellationToken cancellationToken = default);

    Task<Dictionary<string, int>> ReadActiveStrikeCountsByMemberUserIdsAsync(IEnumerable<string> memberUserIds, CancellationToken cancellationToken = default);

    Task<List<ClubMemberStrike>?> ReadStrikesByMemberNicknameAsync(string memberNickname, CancellationToken cancellationToken = default);

    Task<List<ClubMemberStrike>> ReadAllStrikesAsync(CancellationToken cancellationToken = default);

    Task<ClubMemberStrike?> ReadForUpdateByIdAsync(Guid strikeId, CancellationToken cancellationToken = default);

    Task<int> DeleteStrikesBeforeAsync(DateTimeOffset threshold, CancellationToken cancellationToken = default);
}
