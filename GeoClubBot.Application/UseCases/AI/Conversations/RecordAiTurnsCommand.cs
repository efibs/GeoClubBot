using Entities;
using MediatR;
using UseCases.Abstractions;
using UseCases.OutputPorts.Repositories;
using Utilities;

namespace UseCases.UseCases.AI.Conversations;

/// <summary>
/// Stores the question and the answer as a linked pair, once the reply has actually been posted.
///
/// Separate from <see cref="AskAiCommand"/> because the assistant turn is keyed by the Discord message
/// id of the bot's reply, which does not exist until the reply is sent. Writing both together also
/// means a failed or unsent answer leaves nothing behind for a later reply to attach to.
/// </summary>
/// <param name="BotMessageId">
/// The last message of the answer. A long answer is posted as a chain of replies and this is the one
/// a follow-up attaches to, so it is what identifies the turn.
/// </param>
/// <param name="EarlierBotMessageIds">
/// The other messages of a split answer. Recorded so a reaction or a reply on any part of it still
/// resolves to this turn.
/// </param>
public sealed record RecordAiTurnsCommand(
    ulong UserMessageId,
    ulong? ParentMessageId,
    ulong BotMessageId,
    ulong ConversationId,
    ulong ChannelId,
    ulong? GuildId,
    ulong AuthorDiscordUserId,
    ulong BotUserId,
    string Question,
    IReadOnlyList<string> AttachmentImageUrls,
    string Answer,
    string? ModelUsed,
    IReadOnlyList<string> RetrievedSourceUrls,
    IReadOnlyList<string> CitedSourceUrls,
    IReadOnlyList<ulong> EarlierBotMessageIds,
    int Depth) : ICommand<Result>;

public sealed class RecordAiTurnsHandler(IAiConversationRepository conversations)
    : IRequestHandler<RecordAiTurnsCommand, Result>
{
    public Task<Result> Handle(RecordAiTurnsCommand request, CancellationToken cancellationToken)
    {
        conversations.AddTurn(AiConversationTurn.CreateUserTurn(
            request.UserMessageId,
            request.ParentMessageId,
            request.ConversationId,
            request.ChannelId,
            request.GuildId,
            request.AuthorDiscordUserId,
            Truncate(request.Question),
            request.AttachmentImageUrls,
            request.Depth,
            DateTimeOffset.UtcNow));

        conversations.AddTurn(AiConversationTurn.CreateAssistantTurn(
            request.BotMessageId,
            request.UserMessageId,
            request.ConversationId,
            request.ChannelId,
            request.GuildId,
            request.BotUserId,
            Truncate(request.Answer),
            request.ModelUsed,
            request.RetrievedSourceUrls,
            request.CitedSourceUrls,
            request.EarlierBotMessageIds,
            request.Depth + 1,
            DateTimeOffset.UtcNow));

        return Task.FromResult(Result.Success());
    }

    /// <summary>
    /// Keeps content inside the stored column. Truncating is preferable to rejecting: losing the tail
    /// of one long turn is a much smaller problem than breaking the reply chain that depends on it.
    /// </summary>
    private static string Truncate(string value) =>
        value.Length <= Constants.StringLengthConstants.AiConversationContentMaxLength
            ? value
            : value[..Constants.StringLengthConstants.AiConversationContentMaxLength];
}
