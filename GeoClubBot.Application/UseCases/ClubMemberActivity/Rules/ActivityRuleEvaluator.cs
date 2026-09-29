using Entities;

namespace UseCases.UseCases.ClubMemberActivity.Rules;

/// <summary>One classified club activity feed entry of a member.</summary>
public sealed record ClubActivityEntry(ClubXpActivityKind Kind, int Xp, DateTimeOffset RecordedAt);

/// <summary>What a member did in a window, measured against the club's rules.</summary>
/// <param name="RawXp">All club XP in the window.</param>
/// <param name="RuleXp">Club XP that counts for the rules.</param>
/// <param name="Counts">Entries per kind.</param>
/// <param name="Requirements">One result per requirement of the club, empty when it has none.</param>
public sealed record ActivityEvaluation(
    int RawXp,
    int RuleXp,
    IReadOnlyDictionary<ClubXpActivityKind, int> Counts,
    IReadOnlyList<ActivityRequirementResult> Requirements)
{
    public bool AllMet => Requirements.All(r => r.Met);

    public int CountOf(ClubXpActivityKind kind) => Counts.GetValueOrDefault(kind);
}

public static class ActivityRuleEvaluator
{
    /// <summary>
    /// Rule XP of <paramref name="entries"/>: excluded kinds are dropped, and of a capped kind only
    /// the earliest entries up to the cap count.
    /// </summary>
    public static int RuleXp(IEnumerable<ClubActivityEntry> entries, ActivityRules rules) =>
        entries
            .Where(e => !rules.ExcludedFromRuleXp.Contains(e.Kind))
            .GroupBy(e => e.Kind)
            .Sum(g =>
            {
                var ordered = g.OrderBy(e => e.RecordedAt);
                return rules.MaxCountedPerWeek.TryGetValue(g.Key, out var cap)
                    ? ordered.Take(cap).Sum(e => e.Xp)
                    : ordered.Sum(e => e.Xp);
            });

    /// <param name="targetFactor">
    /// Share of the window the member was expected to be active in (0..1): lower for a member who
    /// joined during it or was excused, 0 inside the grace period.
    /// </param>
    public static ActivityEvaluation Evaluate(
        IReadOnlyCollection<ClubActivityEntry> entries,
        ActivityRules rules,
        double targetFactor)
    {
        var counts = entries
            .GroupBy(e => e.Kind)
            .ToDictionary(g => g.Key, g => g.Count());
        var ruleXp = RuleXp(entries, rules);

        var requirements = rules.MinCounts
            .OrderBy(r => r.Key)
            .Select(r => new ActivityRequirementResult(
                r.Key, counts.GetValueOrDefault(r.Key), ScaleTarget(r.Value, targetFactor), r.Value))
            .ToList();

        if (rules.MinRuleXp > 0)
        {
            requirements.Add(new ActivityRequirementResult(
                null, ruleXp, ScaleTarget(rules.MinRuleXp, targetFactor), rules.MinRuleXp));
        }

        return new ActivityEvaluation(entries.Sum(e => e.Xp), ruleXp, counts, requirements);
    }

    public static int ScaleTarget(int required, double targetFactor) =>
        (int)Math.Floor(Math.Clamp(targetFactor, 0, 1) * required);
}

public static class ClubActivityEntries
{
    /// <summary>Classifies raw feed entries for <see cref="ActivityRuleEvaluator"/>.</summary>
    public static List<ClubActivityEntry> From(
        IEnumerable<OutputPorts.GeoGuessr.ReadClubActivitiesItemDto> activities,
        OutputPorts.GeoGuessr.ClubActivityKindClassifier classifier) =>
        activities
            .Select(a => new ClubActivityEntry(classifier.Classify(a), a.XpReward, a.RecordedAt))
            .ToList();
}
