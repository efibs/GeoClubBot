namespace UseCases.OutputPorts.GeoGuessr;

public class DailyMissionDto
{
    public required Guid Id { get; set; }

    public required string Type { get; set; }

    public required string GameMode { get; set; }

    public required int CurrentProgress { get; set; }

    public required int TargetProgress { get; set; }

    public required bool Completed { get; set; }

    public required DateTimeOffset EndDate { get; set; }

    public required int RewardAmount { get; set; }

    public required string RewardType { get; set; }
}
