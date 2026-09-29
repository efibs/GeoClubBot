namespace UseCases.OutputPorts.GeoGuessr;

/// <summary>
/// <c>GET /v4/missions/club/board</c> (and <c>.../board/previous</c>): the weekly club mission board
/// of the club the requesting account belongs to. The path carries no club id, so it must be read
/// with a token of a member of that club.
/// </summary>
public class ClubMissionBoardSnapshotDto
{
    public string? PeriodKey { get; set; }

    public required DateTimeOffset PeriodStart { get; set; }

    public required DateTimeOffset PeriodEnd { get; set; }

    public DateTimeOffset? NextPeriodStart { get; set; }

    /// <summary>1-based number of the board members can claim on; one past the last board once all are cleared.</summary>
    public required int CurrentBoardNumber { get; set; }

    public required bool AllBoardsCleared { get; set; }

    public List<ClubMissionBoardDto> Boards { get; set; } = [];

    /// <summary>Mission texts, keyed by <see cref="ClubMissionTileDto.TemplateId"/>.</summary>
    public Dictionary<string, ClubMissionTemplateDto> Templates { get; set; } = [];

    /// <summary>What the requesting account itself may do — not other members.</summary>
    public ClubMissionBoardYouDto? You { get; set; }
}
