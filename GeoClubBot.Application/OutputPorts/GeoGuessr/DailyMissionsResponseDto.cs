namespace UseCases.OutputPorts.GeoGuessr;

public class DailyMissionsResponseDto
{
    public required List<DailyMissionDto> Missions { get; set; }

    public required DateTimeOffset NextMissionDate { get; set; }
}
