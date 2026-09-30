using System.ComponentModel.DataAnnotations;

namespace Configuration;

public class ActivityRewardConfiguration
{
    public const string SectionName = "ActivityReward";

    [Required]
    public ulong TextChannelId { get; set; }

    [Required]
    public ulong MvpRoleId { get; set; }

    /// <summary>
    /// Rank the MVPs by rule XP (see <c>ActivityChecker:RuleXp</c>) instead of raw club XP.
    /// </summary>
    public bool UseRuleXp { get; set; } = true;
}
