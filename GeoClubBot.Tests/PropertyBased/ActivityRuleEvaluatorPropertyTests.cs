using CsCheck;
using Entities;
using FluentAssertions;
using UseCases.UseCases.ClubMemberActivity.Rules;
using Xunit;

namespace GeoClubBot.Tests.PropertyBased;

/// <summary>
/// Invariants of rule XP over arbitrary weeks: it never exceeds raw XP, excluded kinds never add
/// to it, and a capped kind never contributes more entries than its cap.
/// </summary>
public sealed class ActivityRuleEvaluatorPropertyTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 23, 11, 0, 0, TimeSpan.Zero);

    private static readonly ClubXpActivityKind[] Kinds =
    [
        ClubXpActivityKind.DailyChallengeOrDuel,
        ClubXpActivityKind.BoardMission,
        ClubXpActivityKind.BoardClearBonus,
        ClubXpActivityKind.ClubChallengePlayed,
        ClubXpActivityKind.Unknown
    ];

    private static readonly Gen<List<ClubActivityEntry>> GenEntries =
        Gen.Select(Gen.Int[0, Kinds.Length - 1], Gen.Int[0, 200], Gen.Int[0, 7 * 24 * 60], (kind, xp, minutes) =>
                new ClubActivityEntry(Kinds[kind], xp, Start.AddMinutes(minutes)))
            .List[0, 60];

    private static readonly Gen<ActivityRules> GenRules =
        Gen.Select(Gen.Int[0, 5], Gen.Bool, Gen.Int[0, 300], (cap, excludeBonus, minXp) =>
            new ActivityRules(
                minXp,
                new Dictionary<ClubXpActivityKind, int>
                {
                    [ClubXpActivityKind.DailyChallengeOrDuel] = 6,
                    [ClubXpActivityKind.BoardMission] = 2
                },
                excludeBonus ? new HashSet<ClubXpActivityKind> { ClubXpActivityKind.BoardClearBonus } : [],
                new Dictionary<ClubXpActivityKind, int> { [ClubXpActivityKind.BoardMission] = cap }));

    [Fact]
    public void Rule_xp_never_exceeds_raw_xp() =>
        Gen.Select(GenEntries, GenRules).Sample((entries, rules) =>
        {
            var evaluation = ActivityRuleEvaluator.Evaluate(entries, rules, targetFactor: 1);

            evaluation.RuleXp.Should().BeLessThanOrEqualTo(evaluation.RawXp);
            evaluation.RawXp.Should().Be(entries.Sum(e => e.Xp));
        });

    [Fact]
    public void Capped_and_excluded_kinds_contribute_at_most_what_the_rules_allow() =>
        Gen.Select(GenEntries, GenRules).Sample((entries, rules) =>
        {
            var cap = rules.MaxCountedPerWeek[ClubXpActivityKind.BoardMission];
            var withoutMissions = entries.Where(e => e.Kind != ClubXpActivityKind.BoardMission).ToList();
            var missionXp = ActivityRuleEvaluator.RuleXp(entries, rules) - ActivityRuleEvaluator.RuleXp(withoutMissions, rules);

            // The capped missions are the earliest ones, so their XP is bounded by the cap's worth.
            var bestCapped = entries.Where(e => e.Kind == ClubXpActivityKind.BoardMission)
                .OrderByDescending(e => e.Xp).Take(cap).Sum(e => e.Xp);
            missionXp.Should().BeLessThanOrEqualTo(bestCapped);

            if (rules.ExcludedFromRuleXp.Contains(ClubXpActivityKind.BoardClearBonus))
            {
                var withoutBonus = entries.Where(e => e.Kind != ClubXpActivityKind.BoardClearBonus).ToList();
                ActivityRuleEvaluator.RuleXp(entries, rules).Should().Be(ActivityRuleEvaluator.RuleXp(withoutBonus, rules));
            }
        });

    [Fact]
    public void Targets_never_exceed_the_clubs_requirement_and_shrink_with_the_factor() =>
        Gen.Select(GenEntries, GenRules, Gen.Double[0, 1]).Sample((entries, rules, factor) =>
        {
            var evaluation = ActivityRuleEvaluator.Evaluate(entries, rules, factor);
            var full = ActivityRuleEvaluator.Evaluate(entries, rules, targetFactor: 1);

            evaluation.Requirements.Should().OnlyContain(r => r.Target >= 0 && r.Target <= r.Required);
            evaluation.Requirements.Zip(full.Requirements)
                .Should().OnlyContain(pair => pair.First.Target <= pair.Second.Target);
        });
}
