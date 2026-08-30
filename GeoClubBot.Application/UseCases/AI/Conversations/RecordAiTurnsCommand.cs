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
