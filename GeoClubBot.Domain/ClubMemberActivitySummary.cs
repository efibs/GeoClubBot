namespace Entities;

/// <summary>What a member earned club XP for on one UTC day.</summary>
/// <param name="ChallengeDone">The daily challenge or a duel was played — the day extends the streak.</param>
/// <param name="BoardMissions">Board missions credited to the member that day.</param>
public record DayActivity(DateOnly Date, bool ChallengeDone, int BoardMissions, int Xp);

/// <summary>
/// A member's club activity over a period, for the activity views.
/// </summary>
/// <param name="RuleXp">
/// Club XP that counts for the rules; only set for the current check period, because the weekly
/// caps have no meaning over an arbitrary number of days.
/// </param>
/// <param name="Requirements">
/// Progress towards the club's weekly requirements; only set for the current check period.
/// </param>
/// <param name="HelpedThisWeek">
/// Missions of others the member pressed "help out" on in this board week. Unverified — pressing
/// the button proves nothing. Null when not shown or the board can't be read.
/// </param>
/// <param name="HelpedLastWeek">The same for the previous board week.</param>
public record ClubMemberActivitySummary(
    IReadOnlyList<DayActivity> Days,
    int TotalXp,
    int StreakDays,
    int BoardMissions,
    int BoardClearBonusXp,
    bool JoinedInPeriod,
    DateTimeOffset JoinedDateTime,
    int? RuleXp = null,
    IReadOnlyList<ActivityRequirementResult>? Requirements = null,
    int? HelpedThisWeek = null,
    int? HelpedLastWeek = null,
    DateTimeOffset? PeriodStart = null)
{
    public IReadOnlyList<ActivityRequirementResult> RequirementResults => Requirements ?? [];

    /// <summary>Days on which the member played the daily challenge or a duel.</summary>
    public int NumChallengeDaysDone => Days.Count(d => d.ChallengeDone);

    public bool AllRequirementsMet => RequirementResults.All(r => r.Met);
}
