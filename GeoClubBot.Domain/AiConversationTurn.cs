namespace Entities;

public enum AiTurnRole
{
    User = 0,
    Assistant
}

/// <summary>
/// One message in an AI conversation, mirroring Discord's reply graph.
///
/// A conversation is a tree rather than a list: several people can reply to the same bot answer, and
/// each of those replies starts an independent branch. Storing the parent edge (rather than an
/// ordered list per channel) is what keeps those branches from bleeding into each other — the context
/// for any turn is the path from it up to the root, so two people digging into the same answer never
/// see each other's follow-ups.
/// </summary>
public class AiConversationTurn : BaseEntity
{
    public Guid TurnId { get; private set; }

    /// <summary>
    /// Root message id of the whole tree, denormalised onto every turn so an entire conversation
    /// loads with one indexed query instead of walking parent links one round-trip at a time.
    /// </summary>
    public ulong ConversationId { get; private set; }

    public ulong DiscordMessageId { get; private set; }

    /// <summary>The message this one replied to; null at the root.</summary>
    public ulong? ParentDiscordMessageId { get; private set; }

    public ulong ChannelId { get; private set; }

    public ulong? GuildId { get; private set; }

    public ulong AuthorDiscordUserId { get; private set; }

    public AiTurnRole Role { get; private set; }

    public string Content { get; private set; } = string.Empty;

    /// <summary>
    /// Images attached to this turn. Stored as URLs rather than bytes; note these can expire, so
    /// replaying an old branch may find them dead.
    /// </summary>
    public List<string> ImageUrls { get; private set; } = [];

    /// <summary>Which model answered, on assistant turns. Makes "the bot got worse" diagnosable.</summary>
    public string? ModelId { get; private set; }

    /// <summary>Distance from the root, used to cap runaway threads without re-walking the tree.</summary>
    public int Depth { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static AiConversationTurn CreateUserTurn(
        ulong discordMessageId,
        ulong? parentDiscordMessageId,
        ulong conversationId,
        ulong channelId,
        ulong? guildId,
        ulong authorDiscordUserId,
        string content,
        IEnumerable<string>? imageUrls,
        int depth,
        DateTimeOffset createdAtUtc)
    {
        var urls = imageUrls?.ToList() ?? [];

        // A turn with neither text nor an image carries no information and would waste a request.
        if (string.IsNullOrWhiteSpace(content) && urls.Count == 0)
        {
            throw new ArgumentException("A user turn must have content or at least one image.", nameof(content));
        }

        return Create(discordMessageId, parentDiscordMessageId, conversationId, channelId, guildId,
            authorDiscordUserId, AiTurnRole.User, content, urls, modelId: null, depth, createdAtUtc);
    }

    public static AiConversationTurn CreateAssistantTurn(
        ulong discordMessageId,
        ulong parentDiscordMessageId,
        ulong conversationId,
        ulong channelId,
        ulong? guildId,
        ulong botUserId,
        string content,
        string? modelId,
        int depth,
        DateTimeOffset createdAtUtc) =>
        Create(discordMessageId, parentDiscordMessageId, conversationId, channelId, guildId,
            botUserId, AiTurnRole.Assistant, content, [], modelId, depth, createdAtUtc);

    private static AiConversationTurn Create(
        ulong discordMessageId,
        ulong? parentDiscordMessageId,
        ulong conversationId,
        ulong channelId,
        ulong? guildId,
        ulong authorDiscordUserId,
        AiTurnRole role,
        string content,
        List<string> imageUrls,
        string? modelId,
        int depth,
        DateTimeOffset createdAtUtc)
    {
        if (discordMessageId == 0)
        {
            throw new ArgumentException("A turn must reference a Discord message.", nameof(discordMessageId));
        }

        if (depth < 0)
        {
            throw new ArgumentException("Depth cannot be negative.", nameof(depth));
        }

        return new AiConversationTurn
        {
            TurnId = Guid.NewGuid(),
            DiscordMessageId = discordMessageId,
            ParentDiscordMessageId = parentDiscordMessageId,
            ConversationId = conversationId,
            ChannelId = channelId,
            GuildId = guildId,
            AuthorDiscordUserId = authorDiscordUserId,
            Role = role,
            Content = content,
            ImageUrls = imageUrls,
            ModelId = modelId,
            Depth = depth,
            CreatedAtUtc = createdAtUtc
        };
    }

    private AiConversationTurn()
    {
    }

    public override string ToString() => $"{Role} @{Depth} ({DiscordMessageId})";
}
