using Configuration;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.Abstractions;
using UseCases.OutputPorts.AI;
using UseCases.OutputPorts.Repositories;
using Utilities;

namespace UseCases.UseCases.AI.Conversations;

/// <summary>
/// Answers a Discord message, continuing the reply chain it belongs to.
/// </summary>
/// <param name="ParentDiscordMessageId">The message replied to, or null when this starts a conversation.</param>
public sealed record AskAiCommand(
    ulong DiscordMessageId,
    ulong? ParentDiscordMessageId,
    ulong ChannelId,
    ulong? GuildId,
    ulong AuthorDiscordUserId,
    string Content,
    IReadOnlyList<string> AttachmentImageUrls) : ICommand<Result<AiAnswer>>;

public sealed record AiAnswerImage(string ImageUrl, string SourceUrl, string? Title);

/// <param name="Marker">The number the answer's prose cites, so the two line up.</param>
public sealed record AiAnswerSource(int Marker, string Label, string Url);

/// <param name="ConversationId">Root message id; the caller stores it on both resulting turns.</param>
/// <param name="IsLongThread">True when the branch is deep enough to suggest starting a fresh one.</param>
public sealed record AiAnswer(
    string Text,
    IReadOnlyList<AiAnswerImage> Images,
    IReadOnlyList<AiAnswerSource> Sources,
    string ModelUsed,
    ulong ConversationId,
    int Depth,
    bool IsLongThread);

public sealed partial class AskAiHandler(
    IAiConversationRepository conversations,
    IAiBudgetRepository budget,
    IEmbedder embedder,
    IKnowledgeIndex knowledgeIndex,
    IChatModelCatalog modelCatalog,
    IChatModelClient chatClient,
    IOptions<AiConfiguration> aiConfiguration,
    IOptions<AiConversationConfiguration> conversationConfiguration,
    ILogger<AskAiHandler> logger)
    : IRequestHandler<AskAiCommand, Result<AiAnswer>>
{
    /// <summary>Guide excerpts retrieved per question.</summary>
    private const int RetrievalLimit = 8;

    /// <summary>Sources listed under an answer. Enough to credit the guides used, short of a wall of links.</summary>
    private const int MaxSourcesInReply = 5;

    /// <summary>
    /// One question costs two upstream calls: embedding the query, then generating the answer. Both
    /// are claimed up front so a request that would exceed the daily allowance is refused before any
    /// of it is spent.
    /// </summary>
    private const int RequestsPerQuestion = 2;

    public async Task<Result<AiAnswer>> Handle(AskAiCommand request, CancellationToken cancellationToken)
    {
        var config = aiConfiguration.Value;
        var limits = conversationConfiguration.Value;
        var now = DateTimeOffset.UtcNow;

        var throttled = await IsUserThrottledAsync(request.AuthorDiscordUserId, config, now, cancellationToken)
            .ConfigureAwait(false);
        if (throttled)
        {
            return Error.Conflict("ai.user_throttled",
                $"You've used your AI budget for this hour ({config.MaxRequestsPerUserPerHour}). Try again later.");
        }

        var context = await BuildContextAsync(request, limits, now, cancellationToken).ConfigureAwait(false);

        var reserved = await budget.TryReserveRequestsAsync(
            DateOnly.FromDateTime(now.UtcDateTime),
            RequestsPerQuestion,
            config.OpenRouter.DailyRequestBudget,
            cancellationToken).ConfigureAwait(false);

        if (!reserved)
        {
            return Error.Conflict("ai.budget_exhausted",
                "I'm out of free AI requests for today. They reset at 00:00 UTC.");
        }

        // Deliberately not released on later failure: by this point at least one upstream call has
        // usually been made, and over-counting is far safer than a 429 storm from under-counting.
        var hits = await RetrieveAsync(request, cancellationToken).ConfigureAwait(false);
        if (hits.IsFailure)
        {
            // Deliberately not answered anyway. An empty hit list reads to the model as "the guides do
            // not cover this", so a failed lookup would come back as a confident statement that the
            // corpus is silent on something it documents well — worse than no answer, and it spends
            // the chat request to produce it. Retrieval failures here are transient and retryable.
            return hits.Error;
        }

        var prompt = AiPromptBuilder.Build(
            context, request.Content, request.AttachmentImageUrls, hits.Value);

        // Only ask for a vision-capable model when the user actually attached something; the pool of
        // free models that accept images is far smaller than the pool overall.
        var chain = await modelCatalog.ReadChainAsync(
            new ChatModelRequirements(NeedsImageInput: request.AttachmentImageUrls.Count > 0),
            cancellationToken).ConfigureAwait(false);

        var completion = await chatClient
            .CompleteAsync(new AiChatRequest(chain, prompt.Messages, Temperature: 0.2), cancellationToken)
            .ConfigureAwait(false);

        if (completion.IsFailure)
        {
            // Demote whichever model was asked first so the next question prefers something else.
            modelCatalog.ReportFailure(chain[0]);
            return completion.Error;
        }

        await budget.RecordTokenUsageAsync(
            DateOnly.FromDateTime(now.UtcDateTime),
            completion.Value.Usage.PromptTokens,
            completion.Value.Usage.CompletionTokens,
            cancellationToken).ConfigureAwait(false);

        var (text, citedImages) = AiPromptBuilder.ResolveCitedImages(
            completion.Value.Text, prompt.Images, config.MaxImagesInReply);

        // Resolved from the stripped text, so a marker that only ever appeared inside an [image N]
        // token cannot be mistaken for a plain citation.
        var citedSources = AiPromptBuilder.ResolveCitedSources(text, prompt.Excerpts, MaxSourcesInReply);

        var depth = context.ParentDepth + 1;

        return new AiAnswer(
            text,
            [.. citedImages.Select(image => new AiAnswerImage(image.ImageUrl, image.SourceUrl, image.Title))],
            [.. citedSources.Select(source => new AiAnswerSource(source.Marker, source.Label, source.SourceUrl))],
            completion.Value.ModelUsed,
            ResolveConversationId(context, request),
            depth,
            depth >= limits.LongThreadDepth);
    }

    private async Task<bool> IsUserThrottledAsync(
        ulong authorDiscordUserId,
        AiConfiguration config,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (config.MaxRequestsPerUserPerHour <= 0)
        {
            return false;
        }

        var recent = await conversations
            .CountUserTurnsSinceAsync(authorDiscordUserId, now.AddHours(-1), cancellationToken)
            .ConfigureAwait(false);

        return recent >= config.MaxRequestsPerUserPerHour;
    }

    private async Task<ConversationContext> BuildContextAsync(
        AskAiCommand request,
        AiConversationConfiguration limits,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (request.ParentDiscordMessageId is not { } parentId)
        {
            return ConversationContext.Empty;
        }

        var parent = await conversations.ReadByMessageIdAsync(parentId, cancellationToken).ConfigureAwait(false);
        if (parent is null)
        {
            // A reply to something we never stored — usually history that aged out. Answering fresh
            // is friendlier than refusing, and the user's message still stands on its own.
            return ConversationContext.Empty;
        }

        var turns = await conversations.ReadConversationAsync(parent.ConversationId, cancellationToken)
            .ConfigureAwait(false);

        return ConversationContextBuilder.Build(turns, parentId, limits, now);
    }

    /// <summary>
    /// Retrieval failures degrade to answering without guide context rather than failing the whole
    /// question: a model with no excerpts is still more useful than an error message.
    /// </summary>
    /// <summary>
    /// Returns the retrieved excerpts, or a failure. The distinction matters: "nothing matched" and
    /// "the lookup did not happen" are different answers, and collapsing them into an empty list makes
    /// the model assert the first when the second is true.
    /// </summary>
    private async Task<Result<IReadOnlyList<KnowledgeHit>>> RetrieveAsync(
        AskAiCommand request,
        CancellationToken cancellationToken)
    {
        var inputs = new List<EmbeddingInput> { new TextEmbeddingInput(request.Content) };
        if (request.AttachmentImageUrls.Count > 0)
        {
            inputs.Add(new ImageEmbeddingInput(request.AttachmentImageUrls[0]));
        }

        var embeddings = await embedder.EmbedAsync(inputs, cancellationToken).ConfigureAwait(false);
        if (embeddings.IsFailure)
        {
            return embeddings.Error;
        }

        // Written out rather than folded into the initialiser. ReadOnlyMemory<float> converts
        // implicitly from an array, and the null literal converts to an array, so a conditional
        // expression here takes ReadOnlyMemory<float> as its natural type and the null branch becomes
        // an *empty* memory rather than no value at all. The store then rejects the whole query with
        // "expected dim: 2048, got 0" — a text-only question failing on the image vector it never had.
        ReadOnlyMemory<float>? imageVector = null;
        if (embeddings.Value.Count > 1)
        {
            imageVector = embeddings.Value[1];
        }

        var query = new KnowledgeQuery
        {
            TextVector = embeddings.Value[0],
            ImageVector = imageVector,
            Limit = RetrievalLimit
        };

        try
        {
            return Result<IReadOnlyList<KnowledgeHit>>.Success(
                await knowledgeIndex.SearchAsync(query, cancellationToken).ConfigureAwait(false));
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Logged with the vector widths because the store rejects a malformed query the same way
            // it reports being unreachable, and the two need entirely different fixes. Swallowing this
            // silently is what left an empty query vector looking like an outage.
            LogSearchFailed(
                logger,
                query.TextVector?.Length ?? -1,
                query.ImageVector?.Length ?? -1,
                exception);

            return Error.Unexpected("ai.index_unavailable",
                "I couldn't reach the guide index just now. Please ask again in a moment.");
        }
    }

    /// <summary>
    /// A continued branch keeps its existing root; a fresh conversation is rooted at the message that
    /// started it.
    /// </summary>
    private static ulong ResolveConversationId(ConversationContext context, AskAiCommand request) =>
        context.IsNewConversation ? request.DiscordMessageId : request.ParentDiscordMessageId!.Value;

    [LoggerMessage(LogLevel.Warning,
        "Guide index search failed (text vector: {TextVectorLength}, image vector: {ImageVectorLength}).")]
    static partial void LogSearchFailed(ILogger logger, int textVectorLength, int imageVectorLength, Exception exception);
}
