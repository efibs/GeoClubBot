using Entities;

namespace UseCases.OutputPorts;

public interface IClubChallengeRepository
{
    List<ClubChallengeLink> CreateLatestClubChallengeLinks(ICollection<ClubChallengeLink> links);
    
    Task<List<ClubChallengeLink>> ReadLatestClubChallengeLinksAsync();
    
    void DeleteLatestClubChallengeLinks(ICollection<ClubChallengeLink> links);
}