using System.ComponentModel.DataAnnotations;

namespace Configuration;

public class DailyChallengesConfiguration
{
    public const string SectionName = "DailyChallenges";

    [Required(AllowEmptyStrings = false)]
    public required string Schedule { get; set; }

    [Required]
    public required ulong TextChannelId { get; set; }

    [Required(AllowEmptyStrings = false)]
    public required string ConfigurationFilePath { get; set; }

    [Required]
    public required ulong FirstRoleId { get; set; }

    [Required]
    public required ulong SecondRoleId { get; set; }

    [Required]
    public required ulong ThirdRoleId { get; set; }
}