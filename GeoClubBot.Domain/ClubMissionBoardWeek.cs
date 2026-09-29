namespace Entities;

/// <summary>
/// One mission on the weekly club mission board.
/// </summary>
/// <param name="ClaimedBy">GeoGuessr user id of the claimer; null while the mission is free.</param>
/// <param name="Helpers">
/// User ids of members who pressed "help out". Nothing proves they contributed, so this is shown
/// for information only and never counts towards any requirement.
/// </param>
public sealed record ClubMissionTile(
    Guid MissionId,
    int BoardNumber,
    int Index,
    string TemplateId,
    string Title,
    int TargetProgress,
    int CurrentProgress,
    int RewardXp,
    string? ClaimedBy,
    DateTimeOffset? ClaimedAt,
    IReadOnlyList<string> Helpers,
    DateTimeOffset? HelpRequestedAt,
    bool Completed,
    DateTimeOffset? CompletedAt)
{
    public bool IsFree => ClaimedBy is null && !Completed;

    /// <summary>Claimed but not completed yet — it blocks the board until someone finishes it.</summary>
    public bool IsOpen => ClaimedBy is not null && !Completed;

    public bool HelpRequested => HelpRequestedAt is not null;
}

public sealed record ClubMissionBoard(
    int Number,
    int Size,
    int ClearRewardXp,
    DateTimeOffset? ClearedAt,
    IReadOnlyList<ClubMissionTile> Tiles)
{
    public int CompletedCount => Tiles.Count(t => t.Completed);

    public bool IsCleared => Tiles.Count > 0 && Tiles.All(t => t.Completed);

    /// <summary>Missions not completed yet, whether claimed or free.</summary>
    public int RemainingCount => Tiles.Count - CompletedCount;
}

/// <summary>What a member can do about the board's missions right now.</summary>
public enum ClubMissionClaimState
{
    /// <summary>A mission is free and the member has neither claimed today nor holds an open mission.</summary>
    ClaimAvailable,

    /// <summary>The member holds a claimed mission that is not completed yet, so they cannot claim another.</summary>
    HoldingOpenMission,

    /// <summary>The member already used this claim cycle's claim.</summary>
    ClaimedThisCycle,

    /// <summary>No mission is free: the current board is fully claimed, or every board is cleared.</summary>
    NoneFree
}

/// <summary>
/// The club's mission board for one board week: five boards unlocked one after the other, only the
/// current one claimable.
/// </summary>
/// <param name="NextClaimResetAt">
/// When the daily claim allowance resets next, as GeoGuessr reports it; null when unknown.
/// </param>
public sealed record ClubMissionBoardWeek(
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    int CurrentBoardNumber,
    bool AllBoardsCleared,
    IReadOnlyList<ClubMissionBoard> Boards,
    DateTimeOffset? NextClaimResetAt)
{
    public IEnumerable<ClubMissionTile> AllTiles => Boards.SelectMany(b => b.Tiles);

    /// <summary>The board members can claim on; null once every board is cleared.</summary>
    public ClubMissionBoard? CurrentBoard =>
        AllBoardsCleared ? null : Boards.FirstOrDefault(b => b.Number == CurrentBoardNumber);

    public IReadOnlyList<ClubMissionTile> FreeTiles =>
        CurrentBoard?.Tiles.Where(t => t.IsFree).ToList() ?? [];

    public IReadOnlyList<ClubMissionTile> OpenClaims => AllTiles.Where(t => t.IsOpen).ToList();

    public int TotalTileCount => Boards.Sum(b => b.Tiles.Count);

    public int CompletedTileCount => Boards.Sum(b => b.CompletedCount);

    public IReadOnlyList<ClubMissionTile> ClaimsBy(string userId) =>
        AllTiles.Where(t => t.ClaimedBy == userId).ToList();

    public ClubMissionTile? OpenClaimOf(string userId) =>
        AllTiles.FirstOrDefault(t => t.IsOpen && t.ClaimedBy == userId);

    /// <summary>
    /// Missions of someone else the member pressed "help out" on. Unverified: pressing the button
    /// proves nothing.
    /// </summary>
    public IReadOnlyList<ClubMissionTile> HelpedBy(string userId) =>
        AllTiles.Where(t => t.ClaimedBy != userId && t.Helpers.Contains(userId)).ToList();

    /// <summary>
    /// Start of the claim cycle containing <paramref name="now"/>. Derived from GeoGuessr's own
    /// <see cref="NextClaimResetAt"/> when known — which follows GeoGuessr through daylight saving —
    /// and from <paramref name="fallbackCycleStart"/> otherwise.
    /// </summary>
    public DateTimeOffset ClaimCycleStart(DateTimeOffset now, Func<DateTimeOffset, DateTimeOffset> fallbackCycleStart)
    {
        if (NextClaimResetAt is not { } next)
        {
            return fallbackCycleStart(now);
        }

        // The reported reset can be stale (a cached board read before it passed), so step to the
        // cycle that actually contains now.
        var start = next.AddDays(-1);
        while (start.AddDays(1) <= now)
        {
            start = start.AddDays(1);
        }

        while (start > now)
        {
            start = start.AddDays(-1);
        }

        return start;
    }

    public bool HasClaimedSince(string userId, DateTimeOffset cycleStart) =>
        AllTiles.Any(t => t.ClaimedBy == userId && t.ClaimedAt >= cycleStart);

    public ClubMissionClaimState ClaimStateOf(string userId, DateTimeOffset cycleStart)
    {
        if (OpenClaimOf(userId) is not null)
        {
            return ClubMissionClaimState.HoldingOpenMission;
        }

        if (HasClaimedSince(userId, cycleStart))
        {
            return ClubMissionClaimState.ClaimedThisCycle;
        }

        return FreeTiles.Count > 0 ? ClubMissionClaimState.ClaimAvailable : ClubMissionClaimState.NoneFree;
    }
}
