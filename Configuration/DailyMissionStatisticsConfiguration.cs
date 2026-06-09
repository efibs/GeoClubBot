using System.ComponentModel.DataAnnotations;

namespace Configuration;

public class DailyMissionStatisticsConfiguration
{
    public const string SectionName = "DailyMissionStatistics";

    [Required(AllowEmptyStrings = false)]
    public required string SnapshotSchedule { get; set; }
}
