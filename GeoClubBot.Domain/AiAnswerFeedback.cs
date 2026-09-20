namespace Entities;

public enum AiFeedbackRating
{
    Positive = 0,
    Negative
}

/// <summary>
/// Someone's verdict on one AI answer, stored together with the conversation that produced it.
///
/// This is the only place a conversation is kept permanently. Ordinary turns live in
/// <see cref="AiConversationTurn"/> under a retention sweep, because storing what people asked is a
/// privacy posture rather than an archive; a conversation graduates to being kept only when somebody
/// explicitly rates it, and withdrawing that rating deletes it again.
/// </summary>
public class AiAnswerFeedback : BaseEntity
{
    /// <summary>
    /// Clamps rather than limits: losing the tail of a long comment is a smaller problem than
    /// rejecting feedback somebody took the trouble to write.
    /// </summary>
    private const int CommentMaxLength = 1000;

    /// <summary>
    /// Must match StringLengthConstants.AiConversationContentMaxLength; the Domain layer cannot
    /// reference the Constants project.
    /// </summary>
    private const int ContentMaxLength = 8000;

    public Guid FeedbackId { get; private set; }

    /// <summary>The bot message that was rated; always an assistant turn.</summary>
    public ulong RatedDiscordMessageId { get; private set; }

    public ulong ConversationId { get; private set; }

    public ulong ChannelId { get; private set; }

    public ulong? GuildId { get; private set; }

    public ulong ReviewerDiscordUserId { get; private set; }

    public AiFeedbackRating Rating { get; private set; }

    /// <summary>Optional free text, from the right-click feedback modal.</summary>
    public string? Comment { get; private set; }

    /// <summary>Which model produced the rated answer. Makes "this model got worse" answerable.</summary>
    public string? ModelId { get; private set; }

    /// <summary>Depth of the rated answer in its branch, so deep threads can be told from fresh ones.</summary>
    public int AnswerDepth { get; private set; }

    /// <summary>The branch that produced the answer, oldest first.</summary>
    public List<AiFeedbackTurn> Turns { get; private set; } = [];

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    /// <summary>
    /// Builds the whole aggregate from the branch that was rated.
    /// </summary>
    /// <param name="branchOldestFirst">
    /// The path from the conversation root down to the rated answer, which must be its last element.
    /// Every header field is derived from that answer, so a caller cannot desynchronise them.
    /// </param>
    public static AiAnswerFeedback Create(
        IReadOnlyList<AiConversationTurn> branchOldestFirst,
        ulong reviewerDiscordUserId,
        AiFeedbackRating rating,
        string? comment,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(branchOldestFirst);

        if (branchOldestFirst.Count == 0)
        {
            throw new ArgumentException("Feedback must archive at least one turn.", nameof(branchOldestFirst));
        }

        var answer = branchOldestFirst[^1];

        // You rate an answer, not a question. A user turn as the leaf means the caller resolved the
        // wrong message, and archiving it would put a question in the slot the export reads as the
        // thing being judged.
        if (answer.Role != AiTurnRole.Assistant)
        {
            throw new ArgumentException(
                "The rated turn must be an assistant answer.", nameof(branchOldestFirst));
        }

        var feedback = new AiAnswerFeedback
        {
            FeedbackId = Guid.NewGuid(),
            RatedDiscordMessageId = answer.DiscordMessageId,
            ConversationId = answer.ConversationId,
            ChannelId = answer.ChannelId,
            GuildId = answer.GuildId,
            ReviewerDiscordUserId = reviewerDiscordUserId,
            Rating = rating,
            Comment = ClampComment(comment),
            ModelId = answer.ModelId,
            AnswerDepth = answer.Depth,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc
        };

        feedback.Turns =
        [
            .. branchOldestFirst.Select((turn, ordinal) =>
                AiFeedbackTurn.CreateFrom(feedback.FeedbackId, turn, ordinal, ContentMaxLength))
        ];

        return feedback;
    }

    /// <summary>
    /// Flips the verdict without rebuilding the transcript. The ancestors of a posted message never
    /// change, so the snapshot taken the first time is still exactly what is being re-judged.
    /// </summary>
    public void ChangeRating(AiFeedbackRating rating, DateTimeOffset nowUtc)
    {
        Rating = rating;
        UpdatedAtUtc = nowUtc;
    }

    public void AttachComment(string? comment, DateTimeOffset nowUtc)
    {
        Comment = ClampComment(comment);
        UpdatedAtUtc = nowUtc;
    }

    private static string? ClampComment(string? comment)
    {
        if (string.IsNullOrWhiteSpace(comment))
        {
            return null;
        }

        var trimmed = comment.Trim();
        return trimmed.Length <= CommentMaxLength ? trimmed : trimmed[..CommentMaxLength];
    }

    private AiAnswerFeedback()
    {
    }

    public override string ToString() =>
        $"{Rating} on {RatedDiscordMessageId} by {ReviewerDiscordUserId} ({Turns.Count} turns)";
}
