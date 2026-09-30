using System.ComponentModel.DataAnnotations;

namespace Configuration;

public class ActivityCheckerConfiguration : IValidatableObject
{
    public const string SectionName = "ActivityChecker";

    [Required(AllowEmptyStrings = false)]
    public required string Schedule { get; set; }

    /// <summary>
    /// IANA id of the zone <see cref="Schedule"/> runs in. GeoGuessr resets the mission board at
    /// 12:00 UK time, so "Europe/London" keeps the check just after the reset through daylight saving.
    /// Empty means UTC.
    /// </summary>
    public string? TimeZone { get; set; }

    [Required]
    public required ulong TextChannelId { get; set; }

    /// <summary>Minimum rule XP (see <see cref="RuleXp"/>) per check. 0 disables the XP requirement.</summary>
    [Range(0, int.MaxValue)]
    public int MinXP { get; set; }

    /// <summary>
    /// Minimum number of feed entries of an activity kind per check, keyed by kind name
    /// (<c>DailyChallengeOrDuel</c>, <c>BoardMission</c>, …). Both the daily challenge / duel and a
    /// board mission are credited at most once a day and once per mission, so the count is days on
    /// streak and missions finished respectively. Scaled down like <see cref="MinXP"/> for members
    /// who joined during the week or were excused.
    /// </summary>
    public Dictionary<string, int> Requirements { get; set; } = [];

    /// <summary>How raw club XP turns into rule XP, the figure averages, swaps and the MVP use.</summary>
    public RuleXpConfiguration RuleXp { get; set; } = new();

    /// <summary>
    /// Longest stretch of activity feed a check reads. Bounds the first check (no previous check
    /// time) and a check after downtime; the feed only reaches back a little over two weeks anyway.
    /// </summary>
    public TimeSpan MaxFeedLookback { get; set; } = TimeSpan.FromDays(8);

    [Required]
    public required int GracePeriodDays { get; set; }

    [Required]
    public required int MaxNumStrikes { get; set; }

    [Required]
    public required TimeSpan HistoryKeepTimeSpan { get; set; }

    [Required]
    public required TimeSpan StrikeDecayTimeSpan { get; set; }

    public int? AverageXpTopN { get; set; }

    public int? AverageXpBottomN { get; set; }

    public int AverageXpHistoryDepth { get; set; } = 4;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!string.IsNullOrWhiteSpace(TimeZone) && !TimeZoneInfo.TryFindSystemTimeZoneById(TimeZone, out _))
        {
            yield return new ValidationResult(
                $"{nameof(TimeZone)} '{TimeZone}' is not a known time zone. Use an IANA id such as 'Europe/London'.");
        }

        if (MaxFeedLookback <= TimeSpan.Zero)
        {
            yield return new ValidationResult($"{nameof(MaxFeedLookback)} must be positive.");
        }

        foreach (var result in RuleXpConfiguration.ValidateCounts(Requirements, nameof(Requirements)))
        {
            yield return result;
        }

        foreach (var result in RuleXpConfiguration.ValidateCounts(RuleXp.MaxCountedPerWeek, $"{nameof(RuleXp)}:{nameof(RuleXpConfiguration.MaxCountedPerWeek)}"))
        {
            yield return result;
        }
    }
}

/// <summary>
/// Rule XP is the club XP that counts for the club's own rules. It leaves out XP a member did not
/// really earn themselves (the board-clear bonus goes to whoever finishes a board's last mission)
/// and caps sources the main club has far more of than the second club (board missions run out in
/// a full club), so members of both clubs can be compared fairly.
/// </summary>
public class RuleXpConfiguration
{
    /// <summary>Activity kinds whose XP never counts, by name. E.g. <c>BoardClearBonus</c>.</summary>
    public List<string> ExcludedKinds { get; set; } = [];

    /// <summary>
    /// Kind name → how many entries of that kind count per check (a week); further ones add no
    /// rule XP. E.g. <c>{ "BoardMission": 3 }</c>.
    /// </summary>
    public Dictionary<string, int> MaxCountedPerWeek { get; set; } = [];

    internal static IEnumerable<ValidationResult> ValidateCounts(Dictionary<string, int>? counts, string name)
    {
        if (counts is null)
        {
            yield break;
        }

        foreach (var (kind, count) in counts)
        {
            if (count < 0)
            {
                yield return new ValidationResult($"{name}:{kind} must not be negative.");
            }
        }
    }
}
