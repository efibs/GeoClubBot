using System.ComponentModel.DataAnnotations;

namespace Configuration;

/// <summary>
/// Alerts about board missions that hold the club up: claimed but not finished for a long time,
/// or with help requested and nobody finishing them. Each mission is alerted about at most once
/// per kind of alert.
/// </summary>
public class MissionBoardAlertsConfiguration : IValidatableObject
{
    public const string SectionName = "MissionBoardAlerts";

    public bool Enabled { get; set; }

    /// <summary>
    /// Quartz cron expression of the check. Required even while alerts are off: the job scanner
    /// reads it at start-up.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public required string Schedule { get; set; }

    /// <summary>Channel the alerts are posted to. Null posts none (DMs can still be sent).</summary>
    public ulong? TextChannelId { get; set; }

    /// <summary>Clubs to watch. Empty watches every configured club.</summary>
    public List<Guid> ClubIds { get; set; } = [];

    /// <summary>Alert once a mission has been claimed this long without being completed. Null disables it.</summary>
    public TimeSpan? OpenClaimAlertAfter { get; set; } = TimeSpan.FromHours(6);

    /// <summary>Alert once help has been requested this long ago and the mission is still open. Null disables it.</summary>
    public TimeSpan? HelpRequestAlertAfter { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Only alert while at most this many missions of the current board are left, when a stuck
    /// mission blocks the next board. 0 alerts regardless of how many are left.
    /// </summary>
    [Range(0, 100)]
    public int LastMissionsThreshold { get; set; }

    /// <summary>Mention the claimer in the channel alert when their Discord account is linked.</summary>
    public bool MentionClaimer { get; set; } = true;

    /// <summary>Also DM the claimer when their Discord account is linked.</summary>
    public bool DmClaimer { get; set; }

    /// <summary>
    /// Channel alert for a mission claimed too long ago. Placeholders: <c>{{claimer}}</c>,
    /// <c>{{mission}}</c>, <c>{{progress}}</c>, <c>{{claimed_at}}</c>, <c>{{board}}</c>,
    /// <c>{{remaining}}</c>, <c>{{club}}</c>. Every club's alerts can share one channel, and
    /// GeoGuessr gives each club the same missions, so the defaults lead with the club.
    /// </summary>
    public string OpenClaimMessage { get; set; } =
        "⏳ **[{{club}}]** {{claimer}} claimed **{{mission}}** ({{progress}}) {{claimed_at}} and it's still open. " +
        "{{remaining}} mission(s) left on board {{board}} — finish it or request help!";

    /// <summary>Channel alert for a mission whose claimer asked for help. Same placeholders.</summary>
    public string HelpRequestMessage { get; set; } =
        "🆘 **[{{club}}]** {{claimer}} needs help with **{{mission}}** ({{progress}}) on board {{board}}. Can anyone help out?";

    /// <summary>DM to the claimer for a mission claimed too long ago. Same placeholders.</summary>
    public string OpenClaimDirectMessage { get; set; } =
        "⏳ Your **{{club}}** club mission **{{mission}}** ({{progress}}) has been open since {{claimed_at}}. " +
        "Please finish it or request help, so the club can move on to the next board.";

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (OpenClaimAlertAfter <= TimeSpan.Zero)
        {
            yield return new ValidationResult($"{nameof(OpenClaimAlertAfter)} must be positive or null.");
        }

        if (HelpRequestAlertAfter < TimeSpan.Zero)
        {
            yield return new ValidationResult($"{nameof(HelpRequestAlertAfter)} must not be negative.");
        }
    }
}
