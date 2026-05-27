using Entities;

namespace UseCases.OutputPorts;

public interface IClubChallengeRepository
{
    void AddLatestClubChallengeLinks(IEnumerable<ClubChallengeLink> links);

    Task<List<ClubChallengeLink>> ReadLatestClubChallengeLinksAsync(CancellationToken cancellationToken = default);

    void DeleteLatestClubChallengeLinks(IEnumerable<ClubChallengeLink> links);
}
