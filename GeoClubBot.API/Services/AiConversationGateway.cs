using Configuration;
using Discord;
using Discord.Net;
using Discord.WebSocket;
using GeoClubBot.Discord.InputAdapters.Interactions.AI;
using GeoClubBot.Discord.Services;
using MediatR;
using Microsoft.Extensions.Options;
using UseCases.OutputPorts.AI;
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
    IOptions<AiFeedbackConfiguration> feedbackConfiguration,
    IImageRelay imageRelay,
    IOptions<AiImageRelayConfiguration> imageRelayConfiguration,
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

        var content = CleanContent(message.Content, client.CurrentUser.Id);
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

        var posted = await PostAsync(message, result.Value, imageRelay, imageRelayConfiguration.Value, logger)
            .ConfigureAwait(false);

        await mediator.Send(new RecordAiTurnsCommand(
            message.Id, parentMessageId, posted.LastMessageId, result.Value.ConversationId,
            message.Channel.Id, guildId, message.Author.Id, client.CurrentUser.Id,
            content, attachments, result.Value.Text, result.Value.ModelUsed,
            result.Value.RetrievedSourceUrls, result.Value.CitedSourceUrls,
            posted.EarlierMessageIds, result.Value.Depth,
            result.Value.OfferedChunkIds, result.Value.CitedChunkIds, result.Value.ToolCalls))
            .ConfigureAwait(false);

        // Strictly after the turn is recorded. Offering the affordance before the answer is
        // resolvable would let a fast reader rate a message the feedback handler cannot find yet.
        await OfferFeedbackReactionsAsync(posted.LastMessage).ConfigureAwait(false);
    }

    /// <summary>
    /// Puts 👍/👎 on the answer, because nobody rates what they do not know they can rate.
    ///
    /// Failures are swallowed: the answer has already been delivered, and a guild that has not
    /// granted Add Reactions should lose the affordance, not the reply.
    /// </summary>
    private async Task OfferFeedbackReactionsAsync(IUserMessage? answer)
    {
        var config = feedbackConfiguration.Value;
        if (answer is null || !config.Enabled || !config.PrefillReactions)
        {
            return;
        }

        try
        {
            await answer.AddReactionsAsync(
                [new Emoji(config.PositiveEmoji), new Emoji(config.NegativeEmoji)]).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            LogFeedbackPromptFailed(logger, answer.Id, exception);
        }
    }

    /// <summary>
    /// Posts the answer, replying each chunk to the previous one. The previous implementation sent
    /// continuation chunks as plain channel messages, so a long answer's tail was not a reply and the
    /// message a user would naturally reply to was not the one holding the conversation.
    ///
    /// Relayed pictures are uploaded with the final chunk instead of linked (see
    /// <see cref="AiAnswerPicturePlanner"/> for why), and every file opened for that is closed again
    /// whichever way the post goes.
    /// </summary>
    internal static async Task<PostedAnswer> PostAsync(
        IUserMessage question,
        AiAnswer answer,
        IImageRelay imageRelay,
        AiImageRelayConfiguration relaySettings,
        ILogger logger)
    {
        var files = new Dictionary<string, RelayedImageContent>(StringComparer.Ordinal);

        try
        {
            await OpenStoredPicturesAsync(answer.Images, imageRelay, files, logger).ConfigureAwait(false);

            var plan = AiAnswerPicturePlanner.Plan(
                answer.Images,
                files.ToDictionary(
                    file => file.Key,
                    file => new StoredPicture(
                        file.Value.ContentType,
                        file.Value.Content.CanSeek ? file.Value.Content.Length : null),
                    StringComparer.Ordinal),
                relaySettings);
            LogLinkedPictures(plan, logger);

            var rendering = AiAnswerFormatter.Render(answer, plan);

            IUserMessage replyTarget = question;
            IUserMessage? lastMessage = null;
            var lastMessageId = question.Id;
            var earlierMessageIds = new List<ulong>();

            for (var index = 0; index < rendering.MessageChunks.Count; index++)
            {
                var chunk = rendering.MessageChunks[index];

                // Guide images ride on the final chunk so they appear beneath the whole answer.
                var posted = index < rendering.MessageChunks.Count - 1
                    ? await ReplyWithEmbedsAsync(replyTarget, chunk, embeds: []).ConfigureAwait(false)
                    : await PostFinalChunkAsync(
                        replyTarget, chunk, rendering, files,
                        () => AiAnswerFormatter.Render(answer, plan.WithoutUploads()).Embeds,
                        logger).ConfigureAwait(false);

                if (lastMessage is not null)
                {
                    earlierMessageIds.Add(lastMessage.Id);
                }

                replyTarget = posted;
                lastMessage = posted;
                lastMessageId = posted.Id;
            }

            return new PostedAnswer(lastMessageId, lastMessage, earlierMessageIds);
        }
        finally
        {
            foreach (var file in files.Values)
            {
                await file.Content.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Reads the relayed pictures from disk. One that cannot be read is treated like one that is not
    /// there: it is linked instead, and the answer still goes out.
    /// </summary>
    private static async Task OpenStoredPicturesAsync(
        IReadOnlyList<AiAnswerImage> images,
        IImageRelay imageRelay,
        Dictionary<string, RelayedImageContent> files,
        ILogger logger)
    {
        foreach (var name in AiAnswerPicturePlanner.RelayedNames(images))
        {
            try
            {
                if (await imageRelay.ReadAsync(name).ConfigureAwait(false) is { } content)
                {
                    files[name] = content;
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                LogPictureUnreadable(logger, name, exception);
            }
        }
    }

    /// <summary>
    /// The final chunk, carrying the pictures. With files to upload it is sent through the channel,
    /// because Discord.Net's <c>ReplyAsync</c> takes no attachments; the message reference keeps it a reply.
    /// </summary>
    private static async Task<IUserMessage> PostFinalChunkAsync(
        IUserMessage replyTarget,
        string text,
        AiAnswerRendering rendering,
        IReadOnlyDictionary<string, RelayedImageContent> files,
        Func<IReadOnlyList<Embed>> linkedEmbeds,
        ILogger logger)
    {
        if (rendering.Uploads.Count == 0)
        {
            return await ReplyWithEmbedsAsync(replyTarget, text, rendering.Embeds).ConfigureAwait(false);
        }

        var attachments = rendering.Uploads
            .Select(upload => new FileAttachment(files[upload.StoredName].Content, upload.FileName))
            .ToList();

        try
        {
            return await replyTarget.Channel.SendFilesAsync(
                attachments,
                text,
                embeds: [.. rendering.Embeds],
                // Nothing the model writes may ping anyone.
                allowedMentions: AllowedMentions.None,
                messageReference: new MessageReference(replyTarget.Id)).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsFailedUpload(exception))
        {
            // A failed upload must not cost the answer. Linked pictures may still show; an answer that
            // never arrives shows nothing, and leaves no message a follow-up could reply to.
            LogUploadFailed(logger, replyTarget.Id, rendering.Uploads.Count, exception);
            return await ReplyWithEmbedsAsync(replyTarget, text, linkedEmbeds()).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Whether an upload failed in a way a plain reply with linked pictures very likely would not.
    ///
    /// Discord.Net throws <see cref="HttpException"/> only for an error status (over a server's upload
    /// limit, no Attach Files permission). Everything that goes wrong while the multipart request is being
    /// sent leaves its request queue unchanged: a reset connection or TLS failure
    /// (<see cref="HttpRequestException"/>), its <c>HttpClient</c>'s default 100 s timeout
    /// (<see cref="TaskCanceledException"/>), a picture's file failing mid-read (<see cref="IOException"/>).
    /// A file over the guild's upload limit is refused before sending with
    /// <see cref="ArgumentOutOfRangeException"/>. <see cref="PostAsync"/> takes no cancellation token,
    /// so a cancellation here can only be the transport giving up; if the client is shutting down, the
    /// fallback reply fails the same way and that failure surfaces.
    ///
    /// A transport failure after Discord already stored the message would post the answer twice. That
    /// is the lesser harm: a missing answer cannot be continued by reply.
    /// </summary>
    private static bool IsFailedUpload(Exception exception) =>
        exception is HttpException
            or HttpRequestException
            or IOException
            or ArgumentOutOfRangeException
            or OperationCanceledException;

    private static Task<IUserMessage> ReplyWithEmbedsAsync(IUserMessage replyTarget, string text, IReadOnlyList<Embed> embeds) =>
        replyTarget.ReplyAsync(
            text,
            embeds: embeds.Count > 0 ? [.. embeds] : null,
            // Nothing the model writes may ping anyone.
            allowedMentions: AllowedMentions.None);

    /// <summary>
    /// A relayed picture that is linked rather than uploaded may not show, so each one is logged. A
    /// missing file is a warning: the relay directory is meant to be a volume that outlives redeploys.
    /// </summary>
    private static void LogLinkedPictures(AiAnswerPicturePlan plan, ILogger logger)
    {
        foreach (var picture in plan.Pictures)
        {
            switch (picture.Delivery)
            {
                case AiAnswerPictureDelivery.FileMissing:
                    LogPictureFileMissing(logger, picture.Image.Marker, picture.LinkUrl);
                    break;
                case AiAnswerPictureDelivery.TooLarge or AiAnswerPictureDelivery.OverAllowance:
                    LogPictureNotUploaded(logger, picture.Image.Marker, picture.Delivery, picture.LinkUrl);
                    break;
            }
        }
    }

    /// <param name="EarlierMessageIds">
    /// Every chunk but the last. The turn is keyed by the last message — that is what a reply
    /// attaches to — so these are stored alongside it to keep a reaction on an earlier part of a long
    /// answer from resolving to nothing.
    /// </param>
    internal sealed record PostedAnswer(
        ulong LastMessageId,
        IUserMessage? LastMessage,
        IReadOnlyList<ulong> EarlierMessageIds);

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

    /// <summary>
    /// Strips the bot mention so the question reads naturally to the model — together with the
    /// punctuation that addressed it, or "@Bot, where…" is stored, embedded and replayed as ", where…".
    /// Only a leading mention takes punctuation with it: a reply that merely starts with ":)" keeps it.
    /// </summary>
    internal static string CleanContent(string content, ulong botUserId)
    {
        var mention = $"<@{botUserId}>";
        var nicknameMention = $"<@!{botUserId}>";
        var cleaned = content.Trim();

        foreach (var prefix in (string[])[mention, nicknameMention])
        {
            if (cleaned.StartsWith(prefix, StringComparison.Ordinal))
            {
                cleaned = cleaned[prefix.Length..].TrimStart().TrimStart(',', ':', ';');
                break;
            }
        }

        return cleaned
            .Replace(mention, string.Empty, StringComparison.Ordinal)
            .Replace(nicknameMention, string.Empty, StringComparison.Ordinal)
            .Trim();
    }

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

    [LoggerMessage(LogLevel.Debug, "Could not add the feedback reactions to answer {MessageId}.")]
    static partial void LogFeedbackPromptFailed(ILogger logger, ulong messageId, Exception exception);

    [LoggerMessage(LogLevel.Warning, "Relayed picture {Name} could not be read; linking it instead.")]
    static partial void LogPictureUnreadable(ILogger logger, string name, Exception exception);

    [LoggerMessage(LogLevel.Warning, "Relayed picture [{Marker}] is not on disk; linking {LinkUrl}, which shows only if Discord can reach it.")]
    static partial void LogPictureFileMissing(ILogger logger, int marker, string linkUrl);

    [LoggerMessage(LogLevel.Debug, "Relayed picture [{Marker}] was not uploaded ({Delivery}); linking {LinkUrl}.")]
    static partial void LogPictureNotUploaded(ILogger logger, int marker, AiAnswerPictureDelivery delivery, string linkUrl);

    [LoggerMessage(LogLevel.Warning, "Uploading {Count} picture(s) in reply to {MessageId} failed; posted the answer with linked pictures instead.")]
    static partial void LogUploadFailed(ILogger logger, ulong messageId, int count, Exception exception);
}
