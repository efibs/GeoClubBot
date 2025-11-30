using System.ComponentModel.DataAnnotations;

namespace Configuration;

public class ClubLevelCheckerConfiguration
{
    public const string SectionName = "ClubLevelChecker";
    
    [Required(AllowEmptyStrings = false)]
    public required string Schedule { get; set; }

    [Required()]
    public ulong LevelUpMessageChannelId { get; set; }
}