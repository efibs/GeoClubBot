namespace UseCases.OutputPorts.GeoGuessr;

public class RankedPeakRatingResponseDto
{
    public int? PeakOverallRating { get; set; }

    public GameModeRatingsDto? PeakGameModeRatings { get; set; }
}
