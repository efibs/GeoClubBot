using Entities;

namespace UseCases.OutputPorts;

public interface IClubChallengeRepository
{
    void AddLatestClubChallengeLinks(IEnumerable<ClubChallengeLink> links);

    Task<List<ClubChallengeLink>> ReadLatestClubChallengeLinksAsync();

    void DeleteLatestClubChallengeLinks(IEnumerable<ClubChallengeLink> links);
}
