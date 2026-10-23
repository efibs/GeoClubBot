using System.ComponentModel.DataAnnotations;

namespace Configuration;

/// <summary>
/// Announces new applications to the configured clubs in a Discord channel. Each pending
/// application is announced once; a club opts out with <see cref="GeoGuessrClubEntry.NotifyApplications"/>.
/// </summary>
public class ClubApplicationNotificationsConfiguration : IValidatableObject
{
    public const string SectionName = "ClubApplicationNotifications";

    public bool Enabled { get; set; }

    /// <summary>
    /// Quartz cron expression of the check. Required even while notifications are off: the job
    /// scanner reads it at start-up.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public required string Schedule { get; set; }

    /// <summary>Channel the notifications are posted to.</summary>
    public ulong? TextChannelId { get; set; }

    /// <summary>Role mentioned in each notification (fills <c>{{role}}</c>). Null mentions none.</summary>
    public ulong? RoleIdToPing { get; set; }

    /// <summary>
    /// The notification. Placeholders: <c>{{club}}</c>, <c>{{applicant}}</c> (nickname, plus the
    /// Discord mention when the account is linked), <c>{{nick}}</c>, <c>{{discord}}</c> (the mention
    /// or nothing), <c>{{profile}}</c> (GeoGuessr profile link), <c>{{role}}</c> (the role mention
    /// or nothing). Only the role is ever pinged; the applicant's mention is shown, not notified.
    /// </summary>
    public string Message { get; set; } =
        "📥 **[{{club}}]** New club application from {{applicant}} — {{profile}} {{role}}";

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Enabled && TextChannelId is null)
        {
            yield return new ValidationResult($"{nameof(TextChannelId)} is required while notifications are enabled.");
        }

        if (string.IsNullOrWhiteSpace(Message))
        {
            yield return new ValidationResult($"{nameof(Message)} must not be empty.");
        }
    }
}
