namespace UseCases.OutputPorts.GeoGuessr;

public class ChallengeResultPlayerDto
{
    public required string Id { get; set; }

    public required string Nick { get; set; }

    public required int TotalScoreInPoints { get; set; }

    public required ChallengeResultPlayerDistanceDto TotalDistance { get; set; }

    /// <summary>The player's guesses, one per round in the rounds' order. Null when the list did not carry them.</summary>
    public List<ChallengeResultGuessDto>? Guesses { get; set; }
}
