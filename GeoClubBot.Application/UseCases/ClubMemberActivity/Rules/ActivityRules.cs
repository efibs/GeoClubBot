using Configuration;
using Entities;
using Microsoft.Extensions.Options;

namespace UseCases.UseCases.ClubMemberActivity.Rules;

/// <summary>
/// A club's weekly activity rules, resolved from <c>ActivityChecker</c> and the club's overrides.
/// </summary>
/// <param name="MinRuleXp">Minimum rule XP per check; 0 means no XP requirement.</param>
/// <param name="MinCounts">Minimum entries of a kind per check.</param>
/// <param name="ExcludedFromRuleXp">Kinds whose XP never counts as rule XP.</param>
/// <param name="MaxCountedPerWeek">Kinds whose entries count towards rule XP only up to this many per check.</param>
public sealed record ActivityRules(
    int MinRuleXp,
    IReadOnlyDictionary<ClubXpActivityKind, int> MinCounts,
    IReadOnlySet<ClubXpActivityKind> ExcludedFromRuleXp,
    IReadOnlyDictionary<ClubXpActivityKind, int> MaxCountedPerWeek)
{
    public static ActivityRules None { get; } = new(
        0,
        new Dictionary<ClubXpActivityKind, int>(),
        new HashSet<ClubXpActivityKind>(),
        new Dictionary<ClubXpActivityKind, int>());

    /// <summary>The requirements in short form, e.g. "streak 6 · missions 2"; "none" when there are none.</summary>
    public string Describe()
    {
        var parts = MinCounts
            .OrderBy(r => r.Key)
            .Select(r => $"{r.Key.ShortLabel()} {r.Value}")
            .ToList();

        if (MinRuleXp > 0)
        {
            parts.Add($"{MinRuleXp}XP");
        }

        return parts.Count > 0 ? string.Join(" · ", parts) : "none";
    }

    /// <summary>
    /// Resolves the rules of <paramref name="club"/>. Throws when a kind name is not a
    /// <see cref="ClubXpActivityKind"/>, naming the offending setting.
    /// </summary>
    public static ActivityRules Resolve(ActivityCheckerConfiguration defaults, GeoGuessrClubEntry club)
    {
        var errors = Validate(defaults, club).ToList();
        if (errors.Count != 0)
        {
            throw new InvalidOperationException(string.Join(" ", errors));
        }

        return new ActivityRules(
            club.GetMinXP(defaults),
            club.GetRequirements(defaults)
                .Where(e => e.Value > 0)
                .ToDictionary(e => ParseKind(e.Key)!.Value, e => e.Value),
            club.GetRuleXpExcludedKinds(defaults).Select(k => ParseKind(k)!.Value).ToHashSet(),
            club.GetRuleXpMaxCountedPerWeek(defaults).ToDictionary(e => ParseKind(e.Key)!.Value, e => e.Value));
    }

    /// <summary>Every problem with the kind names of <paramref name="club"/>'s resolved rules.</summary>
    public static IEnumerable<string> Validate(ActivityCheckerConfiguration defaults, GeoGuessrClubEntry club)
    {
        var names = club.GetRequirements(defaults).Keys.Select(k => ("Requirements", k))
            .Concat(club.GetRuleXpExcludedKinds(defaults).Select(k => ("RuleXp:ExcludedKinds", k)))
            .Concat(club.GetRuleXpMaxCountedPerWeek(defaults).Keys.Select(k => ("RuleXp:MaxCountedPerWeek", k)));

        foreach (var (setting, name) in names)
        {
            if (ParseKind(name) is null)
            {
                yield return
                    $"Activity rules of club {club.ClubId}: {setting} names the unknown activity kind '{name}'. " +
                    $"Valid kinds: {string.Join(", ", Enum.GetNames<ClubXpActivityKind>())}.";
            }
        }
    }

    private static ClubXpActivityKind? ParseKind(string name) =>
        Enum.TryParse<ClubXpActivityKind>(name, ignoreCase: true, out var kind) && Enum.IsDefined(kind) ? kind : null;
}

/// <summary>Resolves the configured rules per club.</summary>
public sealed class ActivityRulesProvider(
    IOptions<GeoGuessrConfiguration> geoGuessrConfig,
    IOptions<ActivityCheckerConfiguration> activityCheckerConfig)
{
    public ActivityRules ForClub(Guid clubId) =>
        ActivityRules.Resolve(activityCheckerConfig.Value, geoGuessrConfig.Value.GetClub(clubId));
}

/// <summary>
/// Fails start-up when a rule names an activity kind that does not exist, instead of at the next
/// weekly check.
/// </summary>
public sealed class ActivityRulesOptionsValidator(IOptions<GeoGuessrConfiguration> geoGuessrConfig)
    : IValidateOptions<ActivityCheckerConfiguration>
{
    public ValidateOptionsResult Validate(string? name, ActivityCheckerConfiguration options)
    {
        var errors = geoGuessrConfig.Value.Clubs
            .SelectMany(club => ActivityRules.Validate(options, club))
            .Distinct()
            .ToList();

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
