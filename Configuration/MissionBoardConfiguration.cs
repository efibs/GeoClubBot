using System.ComponentModel.DataAnnotations;

namespace Configuration;

/// <summary>
/// How the weekly club mission board behaves where GeoGuessr does not say so itself.
/// </summary>
public class MissionBoardConfiguration : IValidatableObject
{
    public const string SectionName = "MissionBoard";

    /// <summary>
    /// Local time of day (in <see cref="ClaimResetTimeZone"/>) at which the daily claim allowance
    /// resets. Only a fallback: the board normally reports the next reset itself.
    /// </summary>
    public TimeSpan ClaimResetTimeOfDay { get; set; } = TimeSpan.FromHours(12);

    /// <summary>
    /// IANA id of the zone <see cref="ClaimResetTimeOfDay"/> is in. GeoGuessr resets at 12:00 UK
    /// time, which is 11:00 UTC in summer and 12:00 UTC in winter.
    /// </summary>
    public string ClaimResetTimeZone { get; set; } = "Europe/London";

    public TimeZoneInfo ResolveClaimResetTimeZone() =>
        string.IsNullOrWhiteSpace(ClaimResetTimeZone)
            ? TimeZoneInfo.Utc
            : TimeZoneInfo.FindSystemTimeZoneById(ClaimResetTimeZone);

    /// <summary>Start of the claim cycle that contains <paramref name="now"/>, from the configured reset time.</summary>
    public DateTimeOffset FallbackClaimCycleStart(DateTimeOffset now)
    {
        var zone = ResolveClaimResetTimeZone();
        var localNow = TimeZoneInfo.ConvertTime(now, zone);
        var localReset = localNow.Date + ClaimResetTimeOfDay;
        if (localReset > localNow.DateTime)
        {
            localReset = localReset.AddDays(-1);
        }

        return new DateTimeOffset(localReset, zone.GetUtcOffset(localReset));
    }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (ClaimResetTimeOfDay < TimeSpan.Zero || ClaimResetTimeOfDay >= TimeSpan.FromDays(1))
        {
            yield return new ValidationResult(
                $"{nameof(ClaimResetTimeOfDay)} must be a time of day between 00:00 and 23:59.");
        }

        if (!string.IsNullOrWhiteSpace(ClaimResetTimeZone) && !TimeZoneInfo.TryFindSystemTimeZoneById(ClaimResetTimeZone, out _))
        {
            yield return new ValidationResult(
                $"{nameof(ClaimResetTimeZone)} '{ClaimResetTimeZone}' is not a known time zone. Use an IANA id such as 'Europe/London'.");
        }
    }
}
