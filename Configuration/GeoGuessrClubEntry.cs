using System.ComponentModel.DataAnnotations;

namespace Configuration;

public class GeoGuessrClubEntry
{
    [Required]
    public Guid ClubId { get; set; }

    [Required(AllowEmptyStrings = false)]
    public required string NcfaToken { get; set; }

    public bool IsMain { get; set; }

    public int? MinXP { get; set; }

    /// <summary>
    /// Per-kind requirement overrides, merged over <see cref="ActivityCheckerConfiguration.Requirements"/>
    /// key by key. Set a kind to 0 to drop a global requirement for this club.
    /// </summary>
    public Dictionary<string, int>? Requirements { get; set; }

    /// <summary>
    /// Replaces <see cref="RuleXpConfiguration.ExcludedKinds"/> for this club when set.
    /// </summary>
    public List<string>? RuleXpExcludedKinds { get; set; }

    /// <summary>
    /// Per-kind cap overrides, merged over <see cref="RuleXpConfiguration.MaxCountedPerWeek"/> key by key.
    /// </summary>
    public Dictionary<string, int>? RuleXpMaxCountedPerWeek { get; set; }

    /// <summary>Whether the reminder mentions this club's mission board. Defaults to true.</summary>
    public bool? RemindMissions { get; set; }

    public int? GracePeriodDays { get; set; }

    public int? MaxNumStrikes { get; set; }

    public int? AverageXpTopN { get; set; }

    public int? AverageXpBottomN { get; set; }

    public int? AverageXpHistoryDepth { get; set; }

    public ulong? RoleId { get; set; }

    public int GetMinXP(ActivityCheckerConfiguration defaults) => MinXP ?? defaults.MinXP;

    public Dictionary<string, int> GetRequirements(ActivityCheckerConfiguration defaults) =>
        Merge(defaults.Requirements, Requirements);

    public List<string> GetRuleXpExcludedKinds(ActivityCheckerConfiguration defaults) =>
        RuleXpExcludedKinds ?? defaults.RuleXp.ExcludedKinds;

    public Dictionary<string, int> GetRuleXpMaxCountedPerWeek(ActivityCheckerConfiguration defaults) =>
        Merge(defaults.RuleXp.MaxCountedPerWeek, RuleXpMaxCountedPerWeek);

    public int GetGracePeriodDays(ActivityCheckerConfiguration defaults) => GracePeriodDays ?? defaults.GracePeriodDays;

    public int GetMaxNumStrikes(ActivityCheckerConfiguration defaults) => MaxNumStrikes ?? defaults.MaxNumStrikes;

    public int? GetAverageXpTopN(ActivityCheckerConfiguration defaults) => AverageXpTopN ?? defaults.AverageXpTopN;

    public int? GetAverageXpBottomN(ActivityCheckerConfiguration defaults) => AverageXpBottomN ?? defaults.AverageXpBottomN;

    public int GetAverageXpHistoryDepth(ActivityCheckerConfiguration defaults) => AverageXpHistoryDepth ?? defaults.AverageXpHistoryDepth;

    private static Dictionary<string, int> Merge(Dictionary<string, int> global, Dictionary<string, int>? overrides)
    {
        var merged = new Dictionary<string, int>(global, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in overrides ?? [])
        {
            merged[key] = value;
        }

        return merged;
    }
}
