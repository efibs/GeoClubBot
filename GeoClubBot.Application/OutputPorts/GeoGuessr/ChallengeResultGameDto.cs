namespace UseCases.OutputPorts.GeoGuessr;

public class ChallengeResultGameDto
{
    public required ChallengeResultPlayerDto Player { get; set; }

    /// <summary>The rounds, where GeoGuessr lists them: what the round review reads. Null when the list did not carry them.</summary>
    public List<ChallengeResultRoundDto>? Rounds { get; set; }
}
