namespace UseCases.OutputPorts.GeoGuessr;

public class ClubMissionTileDto
{
    public required Guid MissionId { get; set; }

    public required string TemplateId { get; set; }

    public int Index { get; set; }

    public string? Type { get; set; }

    public string? GameMode { get; set; }

    public string? MapSlug { get; set; }

    public int TargetProgress { get; set; }

    public int Threshold { get; set; }

    public int CurrentProgress { get; set; }

    public int RewardXp { get; set; }

    /// <summary>GeoGuessr user id of the member who claimed the mission; null while it is free.</summary>
    public string? ClaimedBy { get; set; }

    public DateTimeOffset? ClaimedAt { get; set; }

    /// <summary>
    /// User ids of members who pressed "help out". Nothing proves they contributed, and there is
    /// no timestamp.
    /// </summary>
    public List<string> Helpers { get; set; } = [];

    public DateTimeOffset? HelpRequestedAt { get; set; }

    public bool Completed { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }
}
