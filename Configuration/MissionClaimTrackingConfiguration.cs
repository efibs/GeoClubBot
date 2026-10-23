using System.ComponentModel.DataAnnotations;

namespace Configuration;

/// <summary>
/// Records every board mission claim. GeoGuessr only serves the current and the previous board, so
/// this is what the activity views and the weekly report read the mission completion times from.
/// </summary>
public class MissionClaimTrackingConfiguration : IValidatableObject
{
    public const string SectionName = "MissionClaimTracking";

    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Quartz cron expression of the recording. Required even while tracking is off: the job scanner
    /// reads it at start-up. The times are GeoGuessr's own, so the cadence only bounds how stale the
    /// latest claims are, not how accurate they are.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public required string Schedule { get; set; }

    /// <summary>How long recorded claims are kept.</summary>
    public TimeSpan Retention { get; set; } = TimeSpan.FromDays(120);

    /// <summary>
    /// How far back the weekly report's claim-to-completion averages reach. The activity views use
    /// their own period instead (the last N days, the current check period); the report's check
    /// window is about a week, too short to tell a habit from a bad week. Claims count when they
    /// were made within it.
    /// </summary>
    public TimeSpan AverageWindow { get; set; } = TimeSpan.FromDays(14);

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Retention <= TimeSpan.Zero)
        {
            yield return new ValidationResult($"{nameof(Retention)} must be positive.");
        }

        if (AverageWindow <= TimeSpan.Zero)
        {
            yield return new ValidationResult($"{nameof(AverageWindow)} must be positive.");
        }
        else if (AverageWindow > Retention)
        {
            yield return new ValidationResult(
                $"{nameof(AverageWindow)} must not be longer than {nameof(Retention)}: older claims are already deleted.");
        }
    }
}
