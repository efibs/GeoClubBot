using System.ComponentModel.DataAnnotations;

namespace Configuration;

public class DailyMissionReminderConfiguration
{
    public const string SectionName = "DailyMissionReminder";

    [Required(AllowEmptyStrings = false)]
    public required string Schedule { get; set; }

    /// <summary>
    /// Template for the reminder DM. Supports one placeholder, <c>{{outstanding_text}}</c>: what
    /// the user still has to do today - the daily challenge, the daily mission spelled out as
    /// today's actual missions, or both. It ends the sentence and may span several lines, so put
    /// it last. <c>{{mission_text}}</c> is accepted as a legacy alias for the same text.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public required string DefaultMessage { get; set; }

    /// <summary>How many reminders a single user may configure at once.</summary>
    [Range(1, 100)]
    public int MaxRemindersPerUser { get; set; } = 5;
}
