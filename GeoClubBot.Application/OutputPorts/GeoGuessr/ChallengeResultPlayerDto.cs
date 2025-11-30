namespace UseCases.OutputPorts.GeoGuessr;

public class ChallengeResultPlayerDto
{
    public required string Id  { get; set; }

    public required string Nick { get; set; }
   
    public required ChallengeResultPlayerScoreDto TotalScore { get; set; }
    
    public required ChallengeResultPlayerDistanceDto TotalDistance { get; set; }
}