namespace UseCases.OutputPorts.GeoGuessr;

public class ClubMissionBoardDto
{
    public required int Number { get; set; }

    /// <summary>Side length of the square board: 3 means 9 missions.</summary>
    public required int Size { get; set; }

    public int ClearRewardXp { get; set; }

    /// <summary>E.g. <c>Locked</c>, <c>Active</c>, <c>Cleared</c>.</summary>
    public string? Status { get; set; }

    public DateTimeOffset? ClearedAt { get; set; }

    public List<ClubMissionTileDto> Tiles { get; set; } = [];
}
