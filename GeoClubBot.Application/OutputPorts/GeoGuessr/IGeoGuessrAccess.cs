using Entities;

namespace UseCases.OutputPorts.GeoGuessr;

public interface IGeoGuessrAccess
{
    Task<List<ClubMember>> ReadClubMembersAsync(Guid clubId);
    
    Task<Club> ReadClubAsync(Guid clubId);
    
    Task<GeoGuessrUser?> ReadUserAsync(string userId);

    Task<string?> CreateChallengeAsync(int accessLevel, 
        int challengeType, 
        bool forbidMoving, 
        bool forbidRotating, 
        bool forbidZooming, 
        string map,
        int timeLimit);
    
    Task<List<ClubChallengeResultPlayer>?> ReadHighscoresAsync(string challengeId, int limit, int minRounds);
}