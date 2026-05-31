namespace UseCases.OutputPorts.GeoGuessr;

public class RankedProgressResponseDto
{
    public int? DivisionNumber { get; set; }

    public string? DivisionName { get; set; }

    public int? Rating { get; set; }

    public string? Tier { get; set; }

    public GameModeRatingsDto? GameModeRatings { get; set; }

    public required float GuessedFirstRate { get; set; }

    public required int WinStreak { get; set; }

    public required List<bool> LatestGames { get; set; }

    public required List<string> BestCountries { get; set; }

    public required List<string> WorstCountries { get; set; }
}
