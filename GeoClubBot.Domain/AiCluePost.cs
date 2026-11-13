namespace Entities;

/// <summary>
/// A clue of the day that was posted: which indexed chunk, when, where. Kept so the same clue is not
/// posted again for a while, and so a later look at the channel can say what each post was.
/// </summary>
public class AiCluePost : BaseEntity
{
    public int Id { get; private set; }

    /// <summary>The index point the clue was drawn from.</summary>
    public Guid ChunkId { get; private set; }

    /// <summary>The guide the clue came from, as cited under the post.</summary>
    public string SourceUrl { get; private set; } = string.Empty;

    public ulong ChannelId { get; private set; }

    /// <summary>The message posted, when it was; null when the post failed after the clue was chosen.</summary>
    public ulong? MessageId { get; private set; }

    public DateTimeOffset PostedAtUtc { get; private set; }

    public static AiCluePost Create(Guid chunkId, string sourceUrl, ulong channelId, ulong? messageId, DateTimeOffset postedAtUtc) =>
        new()
        {
            ChunkId = chunkId,
            SourceUrl = sourceUrl,
            ChannelId = channelId,
            MessageId = messageId,
            PostedAtUtc = postedAtUtc
        };

    private AiCluePost()
    {
    }
}
