using System.ComponentModel.DataAnnotations;

namespace Configuration;

public class DailyMissionLoggingConfiguration
{
    public const string SectionName = "DailyMissionLogging";

    [Required(AllowEmptyStrings = false)]
    public required string Schedule { get; set; }

    [Range(1, ulong.MaxValue)]
    public ulong ReadableChannelId { get; set; }

    [Required(AllowEmptyStrings = false)]
    public required string ReadableFormat { get; set; }

    [Range(1, ulong.MaxValue)]
    public ulong LookupChannelId { get; set; }

    [Required(AllowEmptyStrings = false)]
    public required string LookupFormat { get; set; }
}
