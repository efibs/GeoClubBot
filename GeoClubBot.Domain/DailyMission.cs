namespace Entities;

public class DailyMission
{
    public int Id { get; set; }

    public required Guid MissionId { get; set; }

    public required string Type { get; set; }

    public required string GameMode { get; set; }

    public required int CurrentProgress { get; set; }

    public required int TargetProgress { get; set; }

    public required bool Completed { get; set; }

    public required DateTimeOffset EndDate { get; set; }

    public required int RewardAmount { get; set; }

    public required string RewardType { get; set; }

    public required DateTimeOffset FetchedAtUtc { get; set; }
}
