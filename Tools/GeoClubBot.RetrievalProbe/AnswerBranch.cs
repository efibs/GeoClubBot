using System.Globalization;
using System.Web;
using Entities;
using UseCases.OutputPorts.Repositories;
using UseCases.UseCases.AI.Conversations;
using UseCases.UseCases.AI.Feedback;

namespace GeoClubBot.RetrievalProbe;

/// <summary>
/// A rated question set up to be asked again: the branch before it as the bot would have stored it, and
/// the message that asks it, replying to that branch's last turn.
///
/// The export keeps no Discord message ids, so the branch gets made-up ones, linked parent to child
/// in transcript order — the branch is a path, so that is the whole tree the bot would walk. It is
/// dated just before now: the bot measures a branch's idle time from its last turn and would answer a
/// month-old one fresh. The answers keep the excerpts they cited where the export has them, so a
/// follow-up is offered them again as it was in production.
/// </summary>
/// <param name="Turns">The branch before the question, oldest first; empty for a fresh question.</param>
/// <param name="Command">The question, replying to the last of <paramref name="Turns"/>.</param>
/// <param name="Notes">What differs from the question as it was asked, for the report.</param>
public sealed record AnswerBranch(
    IReadOnlyList<AiConversationTurn> Turns,
    AskAiCommand Command,
    IReadOnlyList<string> Notes)
{
    /// <summary>Made-up message ids start here; the conversation is rooted at the first.</summary>
    public const ulong FirstMessageId = 1_000;

    /// <summary>Who wrote the answers, when the export does not say.</summary>
    private const ulong BotUserId = 1;

    public static AnswerBranch Create(RatedQuestion rated, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(rated);

        var record = rated.Record;
        var transcript = record.Transcript.OrderBy(turn => turn.Ordinal).ToList();
        var asked = transcript.FindLastIndex(turn => IsRole(turn, "user"));
        var notes = new List<string>();

        var turns = new List<AiConversationTurn>();
        for (var index = 0; index < asked; index++)
        {
            var turn = transcript[index];
            var messageId = FirstMessageId + (ulong)index;
            ulong? parentId = index == 0 ? null : messageId - 1;

            // Minutes apart and ending a minute before now, oldest first.
            var createdAt = now - TimeSpan.FromMinutes(asked - index);

            if (IsRole(turn, "assistant"))
            {
                turns.Add(AiConversationTurn.CreateAssistantTurn(
                    messageId, parentId ?? FirstMessageId, FirstMessageId, record.ChannelId, record.GuildId,
                    turn.AuthorDiscordUserId == 0 ? BotUserId : turn.AuthorDiscordUserId,
                    turn.Content, turn.ModelId, retrievedSourceUrls: null, turn.CitedSourceUrls, chunkMessageIds: null,
                    depth: index, createdAt, turn.OfferedChunkIds, turn.CitedChunkIds));
                continue;
            }

            var images = UsableImages(turn, now, out var dropped);
            if (dropped > 0)
            {
                notes.Add($"A screenshot earlier in the branch (turn {index + 1}) has an expired Discord link and was left out.");
            }

            turns.Add(AiConversationTurn.CreateUserTurn(
                messageId, parentId, FirstMessageId, record.ChannelId, record.GuildId, turn.AuthorDiscordUserId,
                images.Count == 0 && string.IsNullOrWhiteSpace(turn.Content) ? "(a screenshot that is no longer available)" : turn.Content,
                images, depth: index, createdAt));
        }

        var question = transcript[asked];
        var attachments = UsableImages(question, now, out var droppedFromQuestion);
        if (droppedFromQuestion > 0)
        {
            notes.Add("The question's screenshot has an expired Discord link, so it was asked by its text alone.");
        }

        var command = new AskAiCommand(
            FirstMessageId + (ulong)asked,
            turns.Count == 0 ? null : turns[^1].DiscordMessageId,
            record.ChannelId,
            record.GuildId,
            question.AuthorDiscordUserId,
            question.Content,
            attachments);

        return new AnswerBranch(turns, command, notes);
    }

    /// <summary>
    /// The turn's pictures that can still be fetched. A Discord attachment link carries its expiry
    /// (<c>ex</c>, hex seconds) and is dead after it — a model asked to read it fails the whole request —
    /// so an expired one, or one without the parameter, is left out. Other hosts are kept.
    /// </summary>
    public static IReadOnlyList<string> UsableImages(AiFeedbackTurnRecord turn, DateTimeOffset now, out int dropped)
    {
        var usable = turn.ImageUrls.Where(url => !IsExpiredDiscordAttachment(url, now)).ToList();
        dropped = turn.ImageUrls.Count - usable.Count;
        return usable;
    }

    public static bool IsExpiredDiscordAttachment(string url, DateTimeOffset now)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || uri.Host is not ("cdn.discordapp.com" or "media.discordapp.net"))
        {
            return false;
        }

        var expiry = HttpUtility.ParseQueryString(uri.Query)["ex"];
        return expiry is null
               || !long.TryParse(expiry, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var seconds)
               || DateTimeOffset.FromUnixTimeSeconds(seconds) <= now;
    }

    private static bool IsRole(AiFeedbackTurnRecord turn, string role) =>
        string.Equals(turn.Role, role, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// The conversation store <c>answer</c> gives the bot: one branch, seeded from the export, and nothing
/// written back. A fresh one per rated answer, so answers asked side by side cannot see each other.
/// </summary>
public sealed class InMemoryConversationRepository(IReadOnlyList<AiConversationTurn> turns) : IAiConversationRepository
{
    public Task<AiConversationTurn?> ReadByMessageIdAsync(ulong discordMessageId, CancellationToken cancellationToken = default) =>
        Task.FromResult(turns.FirstOrDefault(turn => turn.DiscordMessageId == discordMessageId));

    public Task<AiConversationTurn?> ReadAssistantTurnByAnyMessageIdAsync(ulong discordMessageId, CancellationToken cancellationToken = default) =>
        Task.FromResult(turns.FirstOrDefault(turn => turn.Role == AiTurnRole.Assistant
                                                     && (turn.DiscordMessageId == discordMessageId || turn.ChunkMessageIds.Contains(discordMessageId))));

    public Task<IReadOnlyList<AiConversationTurn>> ReadConversationAsync(ulong conversationId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<AiConversationTurn>>([.. turns.Where(turn => turn.ConversationId == conversationId)]);

    /// <summary>Nothing is stored: the probe only reads.</summary>
    public void AddTurn(AiConversationTurn turn)
    {
    }

    /// <summary>Never throttled: the probe asks one question per record.</summary>
    public Task<int> CountUserTurnsSinceAsync(ulong authorDiscordUserId, DateTimeOffset sinceUtc, CancellationToken cancellationToken = default) =>
        Task.FromResult(0);

    public Task<int> DeleteOlderThanAsync(DateTimeOffset cutoffUtc, CancellationToken cancellationToken = default) =>
        Task.FromResult(0);
}

/// <summary>
/// An allowance that never runs out, and counts what the bot claimed from it. A fresh one per rated
/// answer: each is answered as the first question of a day, so none is answered differently for coming
/// late in the run.
/// </summary>
public sealed class InMemoryBudgetRepository : IAiBudgetRepository
{
    private int _requests;
    private long _promptTokens;
    private long _completionTokens;

    /// <summary>Requests the bot claimed: what this answer would have cost its daily allowance.</summary>
    public int Claimed => _requests;

    public long PromptTokens => _promptTokens;

    public long CompletionTokens => _completionTokens;

    public Task<bool> TryReserveRequestsAsync(DateOnly dateUtc, int amount, int dailyCap, CancellationToken cancellationToken = default)
    {
        Interlocked.Add(ref _requests, amount);
        return Task.FromResult(true);
    }

    public Task ReleaseRequestsAsync(DateOnly dateUtc, int amount, CancellationToken cancellationToken = default)
    {
        Interlocked.Add(ref _requests, -amount);
        return Task.CompletedTask;
    }

    public Task RecordTokenUsageAsync(DateOnly dateUtc, int promptTokens, int completionTokens, CancellationToken cancellationToken = default)
    {
        Interlocked.Add(ref _promptTokens, promptTokens);
        Interlocked.Add(ref _completionTokens, completionTokens);
        return Task.CompletedTask;
    }

    public Task<AiBudgetSnapshot> ReadAsync(DateOnly dateUtc, CancellationToken cancellationToken = default) =>
        Task.FromResult(new AiBudgetSnapshot(dateUtc, _requests, _promptTokens, _completionTokens));
}
