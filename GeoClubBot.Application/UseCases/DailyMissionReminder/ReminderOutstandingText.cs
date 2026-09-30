using Configuration;
using Entities;

namespace UseCases.UseCases.DailyMissionReminder;

/// <summary>What the reminder knows about the user's club missions.</summary>
public enum ReminderMissionState
{
    /// <summary>The board can't be read, the user isn't in a watched club, or the club opted out: leave missions out.</summary>
    Unknown,

    /// <summary>The user's GeoGuessr account isn't linked, so the board can't be matched to them: remind generically.</summary>
    Unlinked,

    ClaimAvailable,
    HoldingOpenMission,
    ClaimedThisCycle,
    NoneFree
}

/// <summary>What the user has done today and what the board looks like for them.</summary>
public sealed record ReminderProgress(
    bool ChallengeDone,
    ReminderMissionState MissionState,
    int FreeCount = 0,
    int BoardNumber = 0,
    DateTimeOffset? NextClaimReset = null,
    ClubMissionTile? OpenMission = null)
{
    public static ReminderProgress Unlinked { get; } = new(false, ReminderMissionState.Unlinked);

    public static ReminderMissionState From(ClubMissionClaimState state) => state switch
    {
        ClubMissionClaimState.ClaimAvailable => ReminderMissionState.ClaimAvailable,
        ClubMissionClaimState.HoldingOpenMission => ReminderMissionState.HoldingOpenMission,
        ClubMissionClaimState.ClaimedThisCycle => ReminderMissionState.ClaimedThisCycle,
        _ => ReminderMissionState.NoneFree
    };
}

/// <summary>Builds the <c>{{outstanding_text}}</c> of a reminder.</summary>
public static class ReminderOutstandingText
{
    /// <summary>What the user still owes, ending the sentence; null when nothing is outstanding.</summary>
    public static string? Build(ReminderProgress progress, DailyMissionReminderConfiguration config)
    {
        var parts = new List<string>();

        if (config.RemindChallenge && !progress.ChallengeDone)
        {
            parts.Add(config.ChallengeText);
        }

        switch (progress.MissionState)
        {
            case ReminderMissionState.ClaimAvailable when config.RemindClaim:
                parts.Add(config.ClaimText
                    .Replace("{{free_count}}", progress.FreeCount.ToString(System.Globalization.CultureInfo.InvariantCulture))
                    .Replace("{{board_number}}", progress.BoardNumber.ToString(System.Globalization.CultureInfo.InvariantCulture))
                    .Replace("{{claim_reset}}", RelativeTimestamp(progress.NextClaimReset, "at the next reset")));
                break;

            case ReminderMissionState.Unlinked when config.RemindClaim:
                parts.Add(config.GenericClaimText);
                break;

            case ReminderMissionState.HoldingOpenMission when config.RemindOpenMission && progress.OpenMission is { } mission:
                parts.Add(config.OpenMissionText
                    .Replace("{{mission_title}}", mission.Title)
                    .Replace("{{progress}}", $"{mission.CurrentProgress}/{mission.TargetProgress}")
                    .Replace("{{claimed_at}}", RelativeTimestamp(mission.ClaimedAt, "earlier")));
                break;
        }

        return parts.Count == 0 ? null : $"{string.Join(config.Joiner, parts)}!";
    }

    private static string RelativeTimestamp(DateTimeOffset? at, string fallback) =>
        at is { } value ? $"<t:{value.ToUnixTimeSeconds()}:R>" : fallback;
}
