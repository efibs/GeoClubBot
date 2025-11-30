using System.ComponentModel.DataAnnotations;

namespace Configuration;

public class ActivityCheckerConfiguration
{
    public const string SectionName = "ActivityChecker";

    [Required(AllowEmptyStrings = false)]
    public required string Schedule { get; set; }

    [Required]
    public required ulong TextChannelId { get; set; }

    [Required]
    public required int MinXP { get; set; }

    [Required]
    public required int GracePeriodDays { get; set; }

    [Required]
    public required int MaxNumStrikes { get; set; }

    [Required(AllowEmptyStrings = false)]
    public required string HistoryKeepTimeSpan { get; set; }

    [Required(AllowEmptyStrings = false)]
    public required string StrikeDecayTimeSpan { get; set; }
}