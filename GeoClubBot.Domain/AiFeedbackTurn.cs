namespace Entities;

/// <summary>
/// One message of an archived conversation, copied out of <see cref="AiConversationTurn"/> when the
/// answer was rated.
///
/// A copy rather than a reference: the working turn store is swept on a retention schedule, and the
/// whole point of the archive is to outlive that sweep. Copying also freezes the conversation as it
/// was judged — a branch keeps growing after someone rates it, and later turns are not part of what
/// they rated.
/// </summary>
public class AiFeedbackTurn : BaseEntity
{
    public Guid FeedbackTurnId { get; private set; }

    public Guid FeedbackId { get; private set; }

    /// <summary>
    /// Position in the branch, oldest first. Stored rather than derived from
    /// <see cref="CreatedAtUtc"/> because both turns of an exchange are written microseconds apart,
    /// which makes timestamp ordering unstable.
    /// </summary>
    public int Ordinal { get; private set; }

    public AiTurnRole Role { get; private set; }

    public ulong AuthorDiscordUserId { get; private set; }

    public ulong DiscordMessageId { get; private set; }

    public string Content { get; private set; } = string.Empty;

    public List<string> ImageUrls { get; private set; } = [];

    public string? ModelId { get; private set; }

    /// <summary>Guides offered to the model, best match first. See <see cref="AiConversationTurn.RetrievedSourceUrls"/>.</summary>
    public List<string> RetrievedSourceUrls { get; private set; } = [];

    /// <summary>Guides the answer pointed at. See <see cref="AiConversationTurn.CitedSourceUrls"/>.</summary>
    public List<string> CitedSourceUrls { get; private set; } = [];

    /// <summary>The original turn's timestamp, so the archive keeps real chronology.</summary>
    public DateTimeOffset CreatedAtUtc { get; private set; }

    /// <summary>
    /// Internal so a transcript can only be built through <see cref="AiAnswerFeedback.Create"/>,
    /// which is what guarantees the ordinals and the header fields agree with the branch.
    /// </summary>
    internal static AiFeedbackTurn CreateFrom(Guid feedbackId, AiConversationTurn turn, int ordinal, int contentMaxLength)
    {
        ArgumentNullException.ThrowIfNull(turn);

        if (ordinal < 0)
        {
            throw new ArgumentException("Ordinal cannot be negative.", nameof(ordinal));
        }

        return new AiFeedbackTurn
        {
            FeedbackTurnId = Guid.NewGuid(),
            FeedbackId = feedbackId,
            Ordinal = ordinal,
            Role = turn.Role,
            AuthorDiscordUserId = turn.AuthorDiscordUserId,
            DiscordMessageId = turn.DiscordMessageId,
            Content = Clamp(turn.Content, contentMaxLength),
            ImageUrls = [.. turn.ImageUrls],
            ModelId = turn.ModelId,
            RetrievedSourceUrls = [.. turn.RetrievedSourceUrls],
            CitedSourceUrls = [.. turn.CitedSourceUrls],
            CreatedAtUtc = turn.CreatedAtUtc
        };
    }

    private static string Clamp(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    private AiFeedbackTurn()
    {
    }

    public override string ToString() => $"#{Ordinal} {Role} ({DiscordMessageId})";
}
