using Entities;

namespace GeoClubBot.Tests.TestBuilders;

/// <summary>Builds mission board weeks for tests, shaped like GeoGuessr's (9/16/25/25/25 missions).</summary>
public static class MissionBoards
{
    public static readonly DateTimeOffset PeriodStart = new(2026, 9, 23, 11, 0, 0, TimeSpan.Zero);

    public static ClubMissionTile Tile(
        int boardNumber = 1,
        int index = 0,
        string? claimedBy = null,
        DateTimeOffset? claimedAt = null,
        bool completed = false,
        DateTimeOffset? helpRequestedAt = null,
        IReadOnlyList<string>? helpers = null,
        string title = "Win 2 Ranked Duels",
        int currentProgress = 0,
        int targetProgress = 2) =>
        new(
            Guid.NewGuid(),
            boardNumber,
            index,
            "ranked-duel-wins",
            title,
            targetProgress,
            completed ? targetProgress : currentProgress,
            20,
            claimedBy,
            claimedBy is null ? null : claimedAt ?? PeriodStart.AddHours(1),
            helpers ?? [],
            helpRequestedAt,
            completed,
            completed ? (claimedAt ?? PeriodStart.AddHours(1)).AddMinutes(30) : null);

    public static ClubMissionBoard Board(int number, params ClubMissionTile[] tiles) =>
        new(number, (int)Math.Sqrt(tiles.Length), 100, tiles.All(t => t.Completed) ? PeriodStart.AddHours(number) : null, tiles);

    /// <summary>A week whose current board is <paramref name="current"/>; earlier boards are cleared, later ones empty.</summary>
    public static ClubMissionBoardWeek Week(
        ClubMissionBoard current,
        DateTimeOffset? nextClaimResetAt = null,
        bool allBoardsCleared = false) =>
        new(
            PeriodStart,
            PeriodStart.AddDays(7),
            current.Number,
            allBoardsCleared,
            [current],
            nextClaimResetAt ?? PeriodStart.AddDays(1));

    /// <summary>A board of <paramref name="count"/> free missions.</summary>
    public static ClubMissionTile[] FreeTiles(int count, int boardNumber = 1) =>
        Enumerable.Range(0, count).Select(i => Tile(boardNumber, i)).ToArray();
}
