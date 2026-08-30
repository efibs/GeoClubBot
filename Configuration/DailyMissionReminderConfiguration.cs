using System.ComponentModel.DataAnnotations;

namespace Configuration;

public class DailyMissionReminderConfiguration
{
    public const string SectionName = "DailyMissionReminder";

    [Required(AllowEmptyStrings = false)]
    public required string Schedule { get; set; }

    /// <summary>
    /// Template for the reminder DM. Supports <c>{{outstanding_text}}</c> (what the user still has
    /// to do today - the daily mission, the daily challenge, or both) and <c>{{mission_text}}</c>
    /// (today's rendered mission list, empty when the mission is already done).
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public required string DefaultMessage { get; set; }

    /// <summary>How many reminders a single user may configure at once.</summary>
    [Range(1, 100)]
    public int MaxRemindersPerUser { get; set; } = 5;
}
