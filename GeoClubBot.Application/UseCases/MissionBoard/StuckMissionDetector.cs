using Configuration;
using Entities;

namespace UseCases.UseCases.MissionBoard;

public sealed record StuckMission(ClubMissionTile Tile, MissionBoardAlertKind Kind);

/// <summary>
/// Finds the board missions that hold the club up: claimed long ago and still open, or with help
/// requested a while ago and nobody finishing them.
/// </summary>
public static class StuckMissionDetector
{
    public static IReadOnlyList<StuckMission> Find(
        ClubMissionBoardWeek board,
        DateTimeOffset now,
        MissionBoardAlertsConfiguration options)
    {
        // A stuck mission only blocks the club while the board is nearly done — before that, others
        // simply claim the free ones — so with a threshold the alerts wait for the last few.
        if (options.LastMissionsThreshold > 0
            && (board.CurrentBoard is not { } current || current.RemainingCount > options.LastMissionsThreshold))
        {
            return [];
        }

        var stuck = new List<StuckMission>();
        foreach (var tile in board.OpenClaims)
        {
            if (options.OpenClaimAlertAfter is { } openAfter
                && tile.ClaimedAt is { } claimedAt
                && now - claimedAt >= openAfter)
            {
                stuck.Add(new StuckMission(tile, MissionBoardAlertKind.OpenClaim));
            }

            if (options.HelpRequestAlertAfter is { } helpAfter
                && tile.HelpRequestedAt is { } helpRequestedAt
                && now - helpRequestedAt >= helpAfter)
            {
                stuck.Add(new StuckMission(tile, MissionBoardAlertKind.HelpRequest));
            }
        }

        return stuck;
    }
}
