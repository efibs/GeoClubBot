namespace Configuration;

/// <summary>
/// Bounds on how much of a reply chain is replayed to the model. Every value here trades answer
/// quality against tokens and against the provider's daily request allowance.
/// </summary>
public class AiConversationConfiguration
{
    /// <summary>Nested under the AI section so the whole feature stays configurable from one place.</summary>
    public const string SectionName = "AI:Conversation";

    /// <summary>Messages of history replayed, newest first. Twelve is roughly six exchanges.</summary>
    public int MaxTurns { get; set; } = 12;

    /// <summary>
    /// Hours of inactivity after which a reply starts a fresh conversation instead of resuming.
    /// Measured from the last message in that branch, not from the root, so a long-running thread
    /// that is still active does not suddenly lose all its context mid-discussion.
    /// </summary>
    public int MaxIdleHours { get; set; } = 24;

    /// <summary>Character budget for replayed history, before the new question and retrieved context.</summary>
    public int MaxContextCharacters { get; set; } = 12_000;

    /// <summary>
    /// Images carried in history. Capped hard because images cost roughly 1800 tokens each — far more
    /// than the text around them — and free models have the smallest context budgets.
    /// </summary>
    public int MaxImagesInContext { get; set; } = 2;

    /// <summary>Depth past which the bot still answers but suggests starting a fresh thread.</summary>
    public int LongThreadDepth { get; set; } = 20;

    /// <summary>Days of history kept before the cleanup job deletes it.</summary>
    public int RetentionDays { get; set; } = 30;
}
