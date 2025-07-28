using Entities;

namespace UseCases.OutputPorts;

public interface IClubChallengeRepository
{
    Task<List<ClubChallengeLink>> CreateLatestClubChallengeLinksAsync(List<ClubChallengeLink> links);
    
    Task<List<ClubChallengeLink>> ReadLatestClubChallengeLinksAsync();
    
    Task<int> DeleteLatestClubChallengeLinksAsync(List<int> linkIds);
}