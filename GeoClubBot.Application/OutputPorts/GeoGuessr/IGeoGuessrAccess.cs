using UseCases.OutputPorts.GeoGuessr.DTOs;

namespace UseCases.OutputPorts.GeoGuessr;

public interface IGeoGuessrAccess
{
    Task<List<GeoGuessrClubMemberDTO>> ReadClubMembersAsync(Guid clubId);
    
    Task<GeoGuessrClubDTO> ReadClubAsync(Guid clubId);
    
    Task<GeoGuessrUserDTO?> ReadUserAsync(string userId);

    Task<GeoGuessrCreateChallengeResponseDTO> CreateChallengeAsync(GeoGuessrCreateChallengeRequestDTO request);
    
    Task<GeoGuessrChallengeResultHighscores?> ReadHighscoresAsync(string challengeId, int limit, int minRounds);
}