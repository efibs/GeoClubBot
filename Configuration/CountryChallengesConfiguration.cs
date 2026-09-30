using System.ComponentModel.DataAnnotations;

namespace Configuration;

/// <summary>
/// The switches of the country challenges. The challenges themselves, their messages and every other
/// setting live in the file at <see cref="ConfigurationFilePath"/>, which is re-read on every run so
/// that editing it needs no restart.
/// </summary>
public class CountryChallengesConfiguration : IValidatableObject
{
    public const string SectionName = "CountryChallenges";

    /// <summary>Master switch. While off, nothing is created or posted and the file is never read.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Quartz cron expression of the daily run. Required even while the feature is off: the job scanner
    /// reads it at start-up.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public required string Schedule { get; set; }

    /// <summary>
    /// IANA id of the zone the schedule runs in. It also decides which date — and so which weekday —
    /// "today" is, so a challenge posted just after local midnight lands on the right day.
    /// </summary>
    public string TimeZone { get; set; } = "UTC";

    /// <summary>Path of the JSON file holding the challenges. Only required while enabled.</summary>
    public string ConfigurationFilePath { get; set; } = string.Empty;

    public TimeZoneInfo ResolveTimeZone() =>
        string.IsNullOrWhiteSpace(TimeZone) ? TimeZoneInfo.Utc : TimeZoneInfo.FindSystemTimeZoneById(TimeZone);

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!string.IsNullOrWhiteSpace(TimeZone) && !TimeZoneInfo.TryFindSystemTimeZoneById(TimeZone, out _))
        {
            yield return new ValidationResult(
                $"{nameof(TimeZone)} '{TimeZone}' is not a known time zone. Use an IANA id such as 'Europe/Berlin'.",
                [nameof(TimeZone)]);
        }

        if (Enabled && string.IsNullOrWhiteSpace(ConfigurationFilePath))
        {
            yield return new ValidationResult(
                $"{nameof(ConfigurationFilePath)} is required while the country challenges are enabled.",
                [nameof(ConfigurationFilePath)]);
        }
    }
}
