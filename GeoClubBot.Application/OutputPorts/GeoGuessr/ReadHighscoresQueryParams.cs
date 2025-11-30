namespace UseCases.OutputPorts.GeoGuessr;

public class ReadHighscoresQueryParams
{
    public bool Friends { get; set; } = false;

    public required int Limit { get; set; }

    public required int MinRounds { get; set; }
}