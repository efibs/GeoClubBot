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
