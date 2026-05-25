using Entities;

namespace UseCases.OutputPorts;

public interface IStrikesRepository
{
    ClubMemberStrike CreateStrike(ClubMemberStrike strike);

    Task<int?> ReadNumberOfActiveStrikesByMemberUserIdAsync(string memberUserId);

    Task<Dictionary<string, int>> ReadActiveStrikeCountsByMemberUserIdsAsync(IEnumerable<string> memberUserIds);

    Task<List<ClubMemberStrike>?> ReadStrikesByMemberNicknameAsync(string memberNickname);

    Task<List<ClubMemberStrike>> ReadAllStrikesAsync();

    Task<ClubMemberStrike?> ReadForUpdateByIdAsync(Guid strikeId);

    Task<int> DeleteStrikesBeforeAsync(DateTimeOffset threshold);
}
