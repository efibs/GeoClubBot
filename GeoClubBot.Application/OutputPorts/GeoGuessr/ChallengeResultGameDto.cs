namespace UseCases.OutputPorts.GeoGuessr;

/// <summary>
/// A player's game, as a challenge's highscores list it and as <c>/v3/games/{token}</c> returns it: the
/// two have the same shape (read 2026-11-21).
/// </summary>
public class ChallengeResultGameDto
{
    /// <summary>The game's own token, which <c>/v3/games/{token}</c> reads it by.</summary>
    public string? Token { get; set; }

    /// <summary>"standard" for a single game, "challenge" for a player's game of a challenge.</summary>
    public string? Type { get; set; }

    /// <summary>"finished" once every round is guessed; a game still being played shows the round on screen.</summary>
    public string? State { get; set; }

    public string? MapName { get; set; }

    /// <summary>The game is over: what a review may read without giving a round away.</summary>
    public bool IsFinished => State is null || string.Equals(State, "finished", StringComparison.OrdinalIgnoreCase);

    public required ChallengeResultPlayerDto Player { get; set; }

    /// <summary>The rounds, where GeoGuessr lists them: what the round review reads. Null when the list did not carry them.</summary>
    public List<ChallengeResultRoundDto>? Rounds { get; set; }

    /// <summary>No moving: the game's rules, which say how the panorama was shown. Null when the list did not carry them.</summary>
    public bool? ForbidMoving { get; set; }

    public bool? ForbidZooming { get; set; }

    /// <summary>No rotating: the player saw only the view each round started with.</summary>
    public bool? ForbidRotating { get; set; }
}
