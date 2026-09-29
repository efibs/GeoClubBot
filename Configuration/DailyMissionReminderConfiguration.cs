using System.ComponentModel.DataAnnotations;

namespace Configuration;

/// <summary>
/// The personal daily reminder: a DM at a time of the user's choosing about what they still owe
/// today — their daily streak (daily challenge or a duel) and a club mission from the board.
/// </summary>
public class DailyMissionReminderConfiguration
{
    public const string SectionName = "DailyMissionReminder";

    [Required(AllowEmptyStrings = false)]
    public required string Schedule { get; set; }

    /// <summary>
    /// Template for the reminder DM. Supports one placeholder, <c>{{outstanding_text}}</c>: what
    /// the user still has to do, built from the texts below and joined with <see cref="Joiner"/>.
    /// It ends the sentence (with "!"), so put it last. <c>{{mission_text}}</c> is accepted as a
    /// legacy alias for the same text.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public required string DefaultMessage { get; set; }

    /// <summary>How many reminders a single user may configure at once.</summary>
    [Range(1, 100)]
    public int MaxRemindersPerUser { get; set; } = 5;

    /// <summary>Remind to play the daily challenge or a duel when it hasn't happened today (UTC).</summary>
    public bool RemindChallenge { get; set; } = true;

    /// <summary>Remind to claim a board mission when one is free and the user hasn't claimed this claim cycle.</summary>
    public bool RemindClaim { get; set; } = true;

    /// <summary>Remind to finish the mission the user holds when it isn't completed yet.</summary>
    public bool RemindOpenMission { get; set; } = true;

    /// <summary>Outstanding part for the daily challenge / duel.</summary>
    [Required(AllowEmptyStrings = false)]
    public string ChallengeText { get; set; } = "play the daily challenge (or a duel)";

    /// <summary>
    /// Outstanding part for claiming a mission. Placeholders: <c>{{free_count}}</c> (free missions on
    /// the current board), <c>{{board_number}}</c>, <c>{{claim_reset}}</c> (when the claim allowance
    /// resets, as a relative Discord timestamp).
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public string ClaimText { get; set; } =
        "claim a club mission ({{free_count}} still free on board {{board_number}}, the daily claim resets {{claim_reset}})";

    /// <summary>
    /// Outstanding part for claiming a mission when the bot can't see the board — the user's
    /// GeoGuessr account isn't linked. No placeholders.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public string GenericClaimText { get; set; } = "claim a club mission if one is free";

    /// <summary>
    /// Outstanding part for a mission the user holds but hasn't completed. Placeholders:
    /// <c>{{mission_title}}</c>, <c>{{progress}}</c> (e.g. <c>2/5</c>), <c>{{claimed_at}}</c>
    /// (relative Discord timestamp).
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public string OpenMissionText { get; set; } =
        "finish your club mission **{{mission_title}}** ({{progress}}) or request help";

    /// <summary>What joins the outstanding parts.</summary>
    public string Joiner { get; set; } = " and ";
}
