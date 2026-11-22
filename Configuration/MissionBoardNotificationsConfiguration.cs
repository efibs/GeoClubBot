using System.ComponentModel.DataAnnotations;

namespace Configuration;

/// <summary>
/// DMs to members who subscribed to their club's mission board (#504): one when the next board is
/// unlocked, and one when a new week's board starts. A member who already claimed this claim cycle
/// is not sent one, since they could not claim anyway.
/// </summary>
public class MissionBoardNotificationsConfiguration
{
    public const string SectionName = "MissionBoardNotifications";

    /// <summary>Off hides the subscription commands and sends nothing.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Quartz cron expression of the safety-net check; a board change pushed over GeoGuessr's socket
    /// is handled at once. Required even while off: the job scanner reads it at start-up.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public required string Schedule { get; set; }

    /// <summary>
    /// DM when the next board is unlocked. Placeholders: <c>{{club}}</c>, <c>{{board}}</c> (its
    /// number), <c>{{free}}</c> (missions free to claim on it).
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public string BoardUnlockedMessage { get; set; } =
        "🔔 Board {{board}} of **{{club}}** is unlocked: {{free}} missions are free to claim.";

    /// <summary>DM when a new board week starts. Same placeholders.</summary>
    [Required(AllowEmptyStrings = false)]
    public string NewWeekMessage { get; set; } =
        "🔔 A new week's mission board is up for **{{club}}**: {{free}} missions are free to claim on board {{board}}.";

    /// <summary>Added below the DM of a subscription that only wanted the next change.</summary>
    public string NextChangeOnlyFooter { get; set; } =
        "This was a one-time alert. Use `/club-stats board-alerts` for another.";
}
