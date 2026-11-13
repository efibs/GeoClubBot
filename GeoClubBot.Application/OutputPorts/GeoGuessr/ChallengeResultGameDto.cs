namespace UseCases.OutputPorts.GeoGuessr;

public class ChallengeResultGameDto
{
    public required ChallengeResultPlayerDto Player { get; set; }

    /// <summary>The rounds, where GeoGuessr lists them: what the round review reads. Null when the list did not carry them.</summary>
    public List<ChallengeResultRoundDto>? Rounds { get; set; }

    /// <summary>No moving: the game's rules, which say how the panorama was shown. Null when the list did not carry them.</summary>
    public bool? ForbidMoving { get; set; }

    public bool? ForbidZooming { get; set; }

    /// <summary>No rotating: the player saw only the view each round started with.</summary>
    public bool? ForbidRotating { get; set; }
}
