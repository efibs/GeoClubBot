namespace Configuration;

/// <summary>
/// Collecting verdicts on the bot's answers, and how long the rated conversations are kept.
///
/// Rated conversations are the only ones stored permanently: ordinary turns are swept on the
/// <see cref="AiConversationConfiguration.RetentionDays"/> schedule, and an answer graduates to the
/// archive only when someone explicitly rates it.
/// </summary>
public class AiFeedbackConfiguration
{
    /// <summary>Nested under the AI section so the whole feature stays configurable from one place.</summary>
    public const string SectionName = "AI:Feedback";

    /// <summary>
    /// Master switch. Turning it off stops the archive growing; the conversation sweep is unaffected
    /// either way, so nothing already collected changes.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Unicode emoji only. Reactions are matched on <c>IEmote.Name</c>, which is the codepoint for a
    /// standard emoji but the alias for a custom guild emote — so a custom emote configured here
    /// would match by name and behave unpredictably across servers.
    /// </summary>
    public string PositiveEmoji { get; set; } = "👍";

    public string NegativeEmoji { get; set; } = "👎";

    /// <summary>
    /// Adds both reactions to the bot's own answer. Discoverability is reaction feedback's only real
    /// weakness — nobody rates what they do not know they can rate — and the cost is two REST calls
    /// per answer, against a daily allowance that already caps answers well below any rate limit.
    /// </summary>
    public bool PrefillReactions { get; set; } = true;

    /// <summary>
    /// Adds ✅ to an answer once a rating is stored for it. It marks the answer, not any one
    /// reviewer, because several people can rate the same message. Worth the extra call because a
    /// reaction that silently failed — on an answer that has aged out of the conversation window,
    /// say — is otherwise indistinguishable from one that worked.
    /// </summary>
    public bool ConfirmWithReaction { get; set; } = true;

    /// <summary>
    /// Days of rated conversations kept. Zero keeps them forever, which is the default: outliving
    /// <see cref="AiConversationConfiguration.RetentionDays"/> is the point of the archive.
    /// </summary>
    public int RetentionDays { get; set; }

    /// <summary>
    /// Entries in one export. Discord caps an attachment at 8 MB, and a transcript is several
    /// kilobytes, so this keeps a large archive from producing a file that cannot be posted.
    /// </summary>
    public int MaxExportRecords { get; set; } = 2000;
}
