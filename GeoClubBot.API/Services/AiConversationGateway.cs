using Configuration;
using Discord;
using Discord.WebSocket;
using GeoClubBot.Discord.InputAdapters.Interactions.AI;
using GeoClubBot.Discord.Services;
using MediatR;
using Microsoft.Extensions.Options;
using UseCases.UseCases.AI;
using UseCases.UseCases.AI.Conversations;
using Utilities;

namespace GeoClubBot.Services;

/// <summary>
/// Listens for messages and drives the AI conversation.
///
/// Replaces the previous listener, which required a mention and explicitly ignored replies, making
/// follow-up questions impossible. A reply to something the bot said now continues that branch with
/// no mention needed; mentioning it starts a new conversation.
/// </summary>
public sealed partial class AiConversationGateway(
    DiscordSocketClient client,
    DiscordBotReadyService botReadyService,
    IServiceScopeFactory scopeFactory,
    IOptions<AiConfiguration> configuration,
    ILogger<AiConversationGateway> logger) : IHostedService
{
    /// <summary>
    /// Serialises turns within one conversation while letting unrelated conversations run in
    /// parallel. Two rapid replies in the same branch would otherwise both read the history before
    /// either wrote its turn, producing sibling answers and spending the budget twice.
    /// </summary>
    private readonly KeyedAsyncLock<ulong> _conversationLocks = new();

    /// <summary>Bounds how many answers are produced at once, protecting the provider's per-minute cap.</summary>
    private readonly SemaphoreSlim _concurrency = new(
        Math.Max(1, configuration.Value.MaxConcurrentRequests),
        Math.Max(1, configuration.Value.MaxConcurrentRequests));

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await botReadyService.DiscordSocketClientReady.ConfigureAwait(false);
        client.MessageReceived += OnMessageReceived;

        // Without this the roster is empty until the refresh job's next tick, and every question in
        // the meantime falls back to the router instead of a chosen model. Fire-and-forget so a slow
        // or unreachable provider cannot hold up start-up.
        _ = Task.Run(RefreshModelCatalogAsync, cancellationToken);
    }

    private async Task RefreshModelCatalogAsync()
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            await scope.ServiceProvider.GetRequiredService<ISender>()
                .Send(new RefreshChatModelCatalogCommand()).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // A missing roster is a degraded state, not a broken one: the fallback router still answers.
            LogCatalogRefreshFailed(logger, ex);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        client.MessageReceived -= OnMessageReceived;
        return Task.CompletedTask;
    }

    private Task OnMessageReceived(SocketMessage socketMessage)
    {
        if (socketMessage is not SocketUserMessage message)
        {
            return Task.CompletedTask;
        }

        // Never block the gateway task: Discord.Net disconnects a client whose handlers stall.
        _ = Task.Run(async () =>
        {
            try
            {
                await HandleMessageAsync(message).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogUnhandled(logger, message.Id, ex);
            }
        });

        return Task.CompletedTask;
    }

    private async Task HandleMessageAsync(SocketUserMessage message)
    {
        if (!ShouldConsider(message))
        {
            return;
        }

        var parentMessageId = ReadReferencedMessageId(message);
        var mentionsBot = message.MentionedUserIds.Contains(client.CurrentUser.Id);

        using var scope = scopeFactory.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<ISender>();

        var isContinuation = parentMessageId is { } parentId
                             && await IsOurTurnAsync(mediator, message, parentId).ConfigureAwait(false);

        if (!isContinuation && !mentionsBot)
        {
            return;
        }

        // A reply that pings us but continues someone else's unrelated message starts fresh.
        var effectiveParentId = isContinuation ? parentMessageId : null;

        var content = CleanContent(message);
        var attachments = ReadImageAttachments(message);
        if (string.IsNullOrWhiteSpace(content) && attachments.Count == 0)
        {
            return;
        }

        // Lock on the conversation root when continuing, and on this message when starting one, so
        // concurrent turns in the same branch cannot interleave.
        var lockKey = effectiveParentId ?? message.Id;

        if (!await _concurrency.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false))
        {
            await message.AddReactionAsync(new Emoji("⏳")).ConfigureAwait(false);
            return;
        }

        try
        {
            using var conversationLock = await _conversationLocks.AcquireAsync(lockKey).ConfigureAwait(false);
            using var typing = message.Channel.EnterTypingState();

            await AnswerAsync(mediator, message, effectiveParentId, content, attachments).ConfigureAwait(false);
        }
        finally
        {
            _concurrency.Release();
        }
    }

    private async Task AnswerAsync(
        ISender mediator,
        SocketUserMessage message,
        ulong? parentMessageId,
        string content,
        IReadOnlyList<string> attachments)
    {
        var guildId = (message.Channel as SocketGuildChannel)?.Guild.Id;

        var result = await mediator.Send(new AskAiCommand(
            message.Id, parentMessageId, message.Channel.Id, guildId,
            message.Author.Id, content, attachments)).ConfigureAwait(false);

        if (result.IsFailure)
        {
            LogAnswerFailed(logger, message.Id, result.Error.Code);
            await message.ReplyAsync(result.Error.Message, allowedMentions: AllowedMentions.None)
                .ConfigureAwait(false);
            return;
        }

        var rendering = AiAnswerFormatter.Render(result.Value);
        var lastMessageId = await PostAsync(message, rendering).ConfigureAwait(false);

        await mediator.Send(new RecordAiTurnsCommand(
            message.Id, parentMessageId, lastMessageId, result.Value.ConversationId,
            message.Channel.Id, guildId, message.Author.Id, client.CurrentUser.Id,
            content, attachments, result.Value.Text, result.Value.ModelUsed, result.Value.Depth))
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Posts the answer, replying each chunk to the previous one. The previous implementation sent
    /// continuation chunks as plain channel messages, so a long answer's tail was not a reply and the
    /// message a user would naturally reply to was not the one holding the conversation.
    /// </summary>
    private static async Task<ulong> PostAsync(SocketUserMessage message, AiAnswerRendering rendering)
    {
        IUserMessage replyTarget = message;
        var lastMessageId = message.Id;

        for (var index = 0; index < rendering.MessageChunks.Count; index++)
        {
            var isLast = index == rendering.MessageChunks.Count - 1;

            var posted = await replyTarget.ReplyAsync(
                rendering.MessageChunks[index],
                // Guide images ride on the final chunk so they appear beneath the whole answer.
                embeds: isLast && rendering.Embeds.Count > 0 ? [.. rendering.Embeds] : null,
                // Nothing the model writes may ping anyone.
                allowedMentions: AllowedMentions.None).ConfigureAwait(false);

            replyTarget = posted;
            lastMessageId = posted.Id;
        }

        return lastMessageId;
    }

    /// <summary>Cheap filters applied before any I/O.</summary>
    private bool ShouldConsider(SocketUserMessage message)
    {
        // Bots and webhooks are excluded so two bots cannot talk each other into an infinite loop.
        if (message.Author.IsBot || message.Author.IsWebhook || message.Author.Id == client.CurrentUser.Id)
        {
            return false;
        }

        // Guild-only for now: direct messages bypass channel allowlists and are an easy budget sink.
        if (message.Channel is not SocketGuildChannel)
        {
            return false;
        }

        var allowedChannels = configuration.Value.AllowedChannelIds;
        return allowedChannels.Count == 0 || allowedChannels.Contains(message.Channel.Id);
    }

    private async Task<bool> IsOurTurnAsync(ISender mediator, SocketUserMessage message, ulong parentId)
    {
        // The cached referenced message answers this without a query when it is available.
        if (message.ReferencedMessage is { } referenced)
        {
            return referenced.Author.Id == client.CurrentUser.Id;
        }

        return await mediator.Send(new IsKnownAiTurnQuery(parentId)).ConfigureAwait(false);
    }

    private static ulong? ReadReferencedMessageId(SocketUserMessage message) =>
        message.Reference?.MessageId.IsSpecified == true ? message.Reference.MessageId.Value : null;

    /// <summary>Strips the bot mention so the question reads naturally to the model.</summary>
    private string CleanContent(SocketUserMessage message) =>
        message.Content
            .Replace($"<@{client.CurrentUser.Id}>", string.Empty, StringComparison.Ordinal)
            .Replace($"<@!{client.CurrentUser.Id}>", string.Empty, StringComparison.Ordinal)
            .Trim();

    private static List<string> ReadImageAttachments(SocketUserMessage message) =>
        [.. message.Attachments
            .Where(attachment => attachment.ContentType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true)
            .Select(attachment => attachment.Url)];

    [LoggerMessage(LogLevel.Warning, "Could not read the AI model roster at start-up; using the fallback router until the next refresh.")]
    static partial void LogCatalogRefreshFailed(ILogger logger, Exception exception);

    [LoggerMessage(LogLevel.Warning, "AI answer for message {MessageId} failed: {ErrorCode}")]
    static partial void LogAnswerFailed(ILogger logger, ulong messageId, string errorCode);

    [LoggerMessage(LogLevel.Error, "Unhandled failure while answering message {MessageId}.")]
    static partial void LogUnhandled(ILogger logger, ulong messageId, Exception exception);
}
