using Entities;

namespace UseCases.OutputPorts.Repositories;

public interface IAiConversationRepository
{
    /// <summary>
    /// Looks up a single turn by its Discord message id. This is how a reply is recognised as a
    /// continuation of a conversation the bot already knows about.
    /// </summary>
    Task<AiConversationTurn?> ReadByMessageIdAsync(ulong discordMessageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds the assistant turn a message belongs to, whether it is the turn's own message or one of
    /// the earlier parts of an answer that was split across several.
    ///
    /// Separate from <see cref="ReadByMessageIdAsync"/> rather than folded into it: the conversation
    /// builder indexes turns by <c>DiscordMessageId</c>, so matching an alias there would make the
    /// parent lookup succeed while the subsequent dictionary probe failed.
    /// </summary>
    Task<AiConversationTurn?> ReadAssistantTurnByAnyMessageIdAsync(
        ulong discordMessageId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Every turn of one conversation tree. Loaded whole in a single indexed read rather than walked
    /// parent-by-parent, which would be one round-trip per ancestor.
    /// </summary>
    Task<IReadOnlyList<AiConversationTurn>> ReadConversationAsync(
        ulong conversationId,
        CancellationToken cancellationToken = default);

    /// <summary>Queues a turn; the unit of work commits it.</summary>
    void AddTurn(AiConversationTurn turn);

    /// <summary>Counts a user's recent questions, for per-user throttling.</summary>
    Task<int> CountUserTurnsSinceAsync(
        ulong authorDiscordUserId,
        DateTimeOffset sinceUtc,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes history past retention. Returns how many turns were removed.</summary>
    Task<int> DeleteOlderThanAsync(DateTimeOffset cutoffUtc, CancellationToken cancellationToken = default);
}
