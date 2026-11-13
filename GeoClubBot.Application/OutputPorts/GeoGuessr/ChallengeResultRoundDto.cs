namespace UseCases.OutputPorts.GeoGuessr;

/// <summary>
/// One round of a game as the highscores list it: where the panorama stands. Every field is optional
/// because the list was read for its players alone until the round review (2026-11-13); the round
/// fields are those GeoGuessr's results pages use, and a game without them is reviewed as having no rounds.
/// </summary>
public class ChallengeResultRoundDto
{
    public double? Lat { get; set; }

    public double? Lng { get; set; }

    public string? PanoId { get; set; }

    public double? Heading { get; set; }

    public double? Pitch { get; set; }

    public double? Zoom { get; set; }

    /// <summary>The round's country as GeoGuessr files it, ISO 3166 alpha-2 in lower case ("vn").</summary>
    public string? StreakLocationCode { get; set; }
}

/// <summary>One guess of the player, in the order of the rounds.</summary>
public class ChallengeResultGuessDto
{
    public double? Lat { get; set; }

    public double? Lng { get; set; }

    public bool? TimedOut { get; set; }

    public bool? TimedOutWithGuess { get; set; }

    public bool? SkippedRound { get; set; }

    public int? RoundScoreInPoints { get; set; }

    public double? DistanceInMeters { get; set; }

    /// <summary>The guessed country, when GeoGuessr says; ISO 3166 alpha-2 in lower case.</summary>
    public string? StreakLocationCode { get; set; }
}
