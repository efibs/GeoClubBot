namespace UseCases.OutputPorts.GeoGuessr;

public class ChallengeResultHighscoresDto
{
    public required List<ChallengeResultItemDto> Items { get; set; }
}