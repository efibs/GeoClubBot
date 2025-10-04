namespace Infrastructure.OutputAdapters.GeoGuessr.DTOs.GetChallengeResults;

public record ChallengeResultPlayerDto(
    string Id, 
    string Nick, 
    ChallengeResultPlayerScoreDto TotalScore, 
    ChallengeResultPlayerDistanceDto TotalDistance);