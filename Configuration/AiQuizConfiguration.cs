using System.ComponentModel.DataAnnotations;

namespace Configuration;

/// <summary>
/// <c>/ai quiz</c> (#491): a guide picture with four answers to pick from. A free vision model rates each
/// picture once (<see cref="Rating"/>): whether it gives its answer away or says too little on its own,
/// how hard it is, and three plausible wrong answers. Members guess once each; a right answer scores the
/// question's difficulty (1–3), kept for the leaderboard.
/// </summary>
public class AiQuizConfiguration
{
    public const string SectionName = "AI:Quiz";

    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Pictures drawn from the index when no rated one fits, to find unrated ones to rate on the spot. Most
    /// of a draw is pictures under a bare heading, which make no question.
    /// </summary>
    [Range(1, 200)]
    public int Draws { get; set; } = 24;

    /// <summary>
    /// Whether the quiz keeps to the channels the assistant answers in (<c>AI:AllowedChannelIds</c>).
    /// Off, or with no such list, it works anywhere the command is visible.
    /// </summary>
    public bool OnlyInAllowedChannels { get; set; } = true;

    /// <summary>How long after posting the answer is shown to everyone, unless the asker or an admin reveals it sooner.</summary>
    [Range(1, 1_440)]
    public int RevealAfterMinutes { get; set; } = 10;

    /// <summary>
    /// Unrated pictures a member's quiz may have rated while they wait, when no rated one fits their
    /// choice. Each is one request from the day's whole allowance and about 30 s of waiting; none left,
    /// the quiz says it is not ready.
    /// </summary>
    [Range(0, 10)]
    public int OnTheSpotRatings { get; set; } = 1;

    /// <summary>Members listed on each board of <c>/ai quiz-scores</c>.</summary>
    [Range(1, 25)]
    public int LeaderboardSize { get; set; } = 10;

    /// <summary>The rolling window of the leaderboard's second board, beside all time.</summary>
    [Range(1, 365)]
    public int LeaderboardRecentDays { get; set; } = 7;

    [Required]
    public AiQuizRatingConfiguration Rating { get; set; } = new();
}

/// <summary>
/// The vision model's look at each quiz picture: which questions it can fairly ask, how hard each is, and
/// the wrong answers. Run by <c>AiQuizRatingJob</c> after the caption job, within its own share of the day.
/// </summary>
public class AiQuizRatingConfiguration
{
    /// <summary>Whether the background job rates pictures. Off, pictures are only rated when a quiz needs one.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Share of the daily allowance the background job may spend, as a ceiling on the day's shared counter.</summary>
    [Range(1, 100)]
    public int MaxDailyBudgetPercent { get; set; } = 20;

    /// <summary>Pictures rated per run, whatever the allowance.</summary>
    [Range(1, 10_000)]
    public int MaxPicturesPerRun { get; set; } = 60;

    /// <summary>
    /// Which generation of ratings is current. Raise it after a prompt change: every picture is rated again
    /// over the following runs, and only current ratings are asked meanwhile.
    /// </summary>
    [Range(1, int.MaxValue)]
    public int Version { get; set; } = 1;

    /// <summary>How long one picture may take the model before the next is asked.</summary>
    [Range(5, 600)]
    public int TimeoutSeconds { get; set; } = 90;

    /// <summary>
    /// How much a reasoning model may think before rating: <c>off</c>, <c>low</c>, <c>medium</c>, <c>high</c>, or
    /// <c>default</c> for the model's own. Free vision models reason by default, and one with no bound
    /// spent a whole reply budget thinking and wrote nothing (#491).
    /// </summary>
    [RegularExpression("^(?i)(off|low|medium|high|default)$")]
    public string Reasoning { get; set; } = "low";

    /// <summary>
    /// The reply budget in tokens, thinking included. The rating itself is about 80; on <c>low</c> the free
    /// vision model thought for 2,300–4,000+ tokens (about 30 s), so 4,000 lost some replies.
    /// </summary>
    [Range(100, 32_000)]
    public int MaxReplyTokens { get; set; } = 8_000;

    /// <summary>Unusable or failed replies in a row before a run stops for the day.</summary>
    [Range(1, 100)]
    public int MaxConsecutiveFailures { get; set; } = 3;
}
