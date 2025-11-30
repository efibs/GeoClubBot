namespace UseCases.OutputPorts.GeoGuessr;

public class UserCompetitiveDto
{
    public required int Elo { get; set; }

    public required int Rating { get; set; }

    public required int LastRatingChange { get; set; }
}