namespace Entities;

/// <param name="XpSinceLastUpdate">Raw club XP since the previous check, from the XP snapshots.</param>
/// <param name="IndividualTarget">The member's rule-XP target (0 when the club has no XP requirement).</param>
/// <param name="RuleXp">Club XP that counts for the rules (see <c>ActivityChecker:RuleXp</c>); null when not evaluated.</param>
/// <param name="Requirements">Every requirement of the club and how the member did; empty when not evaluated.</param>
public record ClubMemberActivityStatus(
    string Nickname,
    string UserId,
    bool TargetAchieved,
    int XpSinceLastUpdate,
    int NumStrikes,
    bool IsOutOfStrikes,
    int IndividualTarget,
    string? IndividualTargetReason,
    int? RuleXp = null,
    IReadOnlyList<ActivityRequirementResult>? Requirements = null)
{
    public IReadOnlyList<ActivityRequirementResult> RequirementResults => Requirements ?? [];

    /// <summary>The XP figure used for rankings: rule XP when evaluated, raw XP otherwise.</summary>
    public int RankingXp => RuleXp ?? XpSinceLastUpdate;

    /// <summary>The requirements the member missed, e.g. "streak 4/6 · missions 1/2"; null when none.</summary>
    public string? FailedRequirementsText =>
        RequirementResults.Where(r => !r.Met).ToList() is { Count: > 0 } failed
            ? string.Join(" · ", failed.Select(r => $"{r.Label} {r.Actual}/{r.Target}"))
            : null;
}
