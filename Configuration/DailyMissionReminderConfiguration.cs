using System.ComponentModel.DataAnnotations;

namespace Configuration;

public class DailyMissionReminderConfiguration
{
    public const string SectionName = "DailyMissionReminder";

    [Required(AllowEmptyStrings = false)]
    public required string Schedule { get; set; }

    [Required(AllowEmptyStrings = false)]
    public required string DefaultMessage { get; set; }
}
