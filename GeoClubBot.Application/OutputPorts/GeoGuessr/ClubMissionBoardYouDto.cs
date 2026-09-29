namespace UseCases.OutputPorts.GeoGuessr;

/// <summary>The requesting account's own view of the board.</summary>
public class ClubMissionBoardYouDto
{
    public List<Guid> ClaimedMissionIds { get; set; } = [];

    public List<Guid> HelpingMissionIds { get; set; } = [];

    public bool CanClaim { get; set; }

    public bool CanHelp { get; set; }

    /// <summary>When the daily claim allowance resets next. The same moment for every member.</summary>
    public DateTimeOffset? NextDayAt { get; set; }
}
