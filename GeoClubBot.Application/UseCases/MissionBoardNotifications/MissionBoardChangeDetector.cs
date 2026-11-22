using Entities;

namespace UseCases.UseCases.MissionBoardNotifications;

public enum MissionBoardChangeKind
{
    /// <summary>Nothing worth storing: the same board, or an older read than the one stored.</summary>
    None,

    /// <summary>The first look at this club's board: stored, but nobody is told, since nothing is known to have changed.</summary>
    FirstSeen,

    /// <summary>Changed in a way nobody is told about (e.g. the last board cleared); stored.</summary>
    Updated,

    /// <summary>The next board of the same week is now the one to claim on.</summary>
    BoardUnlocked,

    /// <summary>A new board week started.</summary>
    NewWeek,
}

/// <summary>What changed on a club's board since the last observation, and whether to store the new state.</summary>
public sealed record MissionBoardChange(MissionBoardChangeKind Kind)
{
    public bool ShouldStore => Kind != MissionBoardChangeKind.None;

    public bool ShouldNotify => Kind is MissionBoardChangeKind.BoardUnlocked or MissionBoardChangeKind.NewWeek;
}

/// <summary>
/// Compares a club's board with the state last stored (#504). Every claim and every bit of progress
/// pushes a board update, so only the two moments members wait for count: the next board unlocked
/// and a new week. A board read that is older than the stored one (a cached copy) changes nothing,
/// so the state only ever moves forward and a change is reported once.
/// </summary>
public static class MissionBoardChangeDetector
{
    public static MissionBoardChange Detect(ClubMissionBoardObservation? last, ClubMissionBoardWeek week)
    {
        if (last is null)
        {
            return new MissionBoardChange(MissionBoardChangeKind.FirstSeen);
        }

        if (week.PeriodStart > last.PeriodStart)
        {
            return new MissionBoardChange(Notifiable(week, MissionBoardChangeKind.NewWeek));
        }

        if (week.PeriodStart < last.PeriodStart || week.CurrentBoardNumber < last.CurrentBoardNumber)
        {
            return new MissionBoardChange(MissionBoardChangeKind.None);
        }

        if (week.CurrentBoardNumber > last.CurrentBoardNumber)
        {
            return new MissionBoardChange(Notifiable(week, MissionBoardChangeKind.BoardUnlocked));
        }

        return new MissionBoardChange(week.AllBoardsCleared != last.AllBoardsCleared
            ? MissionBoardChangeKind.Updated
            : MissionBoardChangeKind.None);
    }

    /// <summary>A change with nothing to claim (every board cleared) is stored but not told.</summary>
    private static MissionBoardChangeKind Notifiable(ClubMissionBoardWeek week, MissionBoardChangeKind kind) =>
        week.FreeTiles.Count > 0 ? kind : MissionBoardChangeKind.Updated;
}
