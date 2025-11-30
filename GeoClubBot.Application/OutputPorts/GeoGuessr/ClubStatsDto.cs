namespace UseCases.OutputPorts.GeoGuessr;

public class ClubStatsDto
{
    public required Guid ClubId { get; set; }

    public required int TotalXp { get; set; }

    public required double ChangePercentXp { get; set; }

    public required int TotalGamesPlayed { get; set; }

    public required double ChangePercentGamesPlayed { get; set; }

    public required int TotalWins { get; set; }

    public required double ChangePercentWins { get; set; }

    public required int TotalPerfectGuesses { get; set; }

    public required double ChangePercentPerfectGuesses { get; set; }

    public required int GlobalXpRank { get; set; }

    public required int TotalClubs { get; set; }

    public required ClubAverageDivisionDto AverageDivision { get; set; }
}