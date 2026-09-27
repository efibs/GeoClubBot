using System.Diagnostics;
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

/// <param name="Marker">The number the prose cites this picture by, shown on its embed so the two line up.</param>
public sealed record AiAnswerImage(int Marker, string ImageUrl, string SourceUrl, string? Title);

/// <param name="Marker">
/// The number the answer's prose cites, so the two line up — or <c>null</c> when the model cited
/// nothing and the guides are being credited on its behalf, where a number would point nowhere.
/// </param>
/// <param name="IsImage">A cited picture, listed alongside the text sources so the numbers stay contiguous.</param>
public sealed record AiAnswerSource(int? Marker, string Label, string Url, bool IsImage = false);

/// <param name="ConversationId">Root message id; the caller stores it on both resulting turns.</param>
/// <param name="IsLongThread">True when the branch is deep enough to suggest starting a fresh one.</param>
/// <param name="RetrievedSourceUrls">
/// Every guide offered to the model, best match first — not only the ones it used. Stored rather
/// than only rendered, because a rated-bad answer has two causes that read identically in the text:
/// the right guide was never retrieved, or it was retrieved and the model ignored it.
/// </param>
/// <param name="CitedSourceUrls">The guides the answer actually pointed at, as shown beneath it.</param>
public sealed record AiAnswer(
    string Text,
    IReadOnlyList<AiAnswerImage> Images,
    IReadOnlyList<AiAnswerSource> Sources,
    string ModelUsed,
    ulong ConversationId,
    int Depth,
    bool IsLongThread,
    IReadOnlyList<string> RetrievedSourceUrls,
    IReadOnlyList<string> CitedSourceUrls);

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
    /// <summary>
    /// Guide excerpts retrieved per question. Every one the answer cites is listed under it — the
    /// numbers in the prose must all resolve — so this also bounds the length of that list.
    /// </summary>
    private const int RetrievalLimit = 8;

    /// <summary>
    /// Guides credited when the model cited none. These are the best matches rather than anything the
    /// answer pointed at, so a long list would overstate them.
    /// </summary>
    private const int MaxUncitedSourcesInReply = 3;

    /// <summary>
    /// One question costs two upstream calls: embedding the query, then generating the answer. Both
    /// are claimed up front so a request that would exceed the daily allowance is refused before any
    /// of it is spent. A retry claims its own request when it happens.
    /// </summary>
    private const int RequestsPerQuestion = 2;

    /// <summary>
    /// A first chain that fails, or answers with something that is not an answer, gets one more try
    /// against models not yet asked. One, because each costs a request from the daily allowance and a
    /// provider failing twice in a row is rarely going to answer a third time.
    /// </summary>
    private const int MaxChatAttempts = 2;

    private const int RequestsPerRetry = 1;

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

        // A vision-capable model only when a picture actually travels with the request — the question's
        // own attachment, or a screenshot replayed from earlier in the branch, which a follow-up sends
        // without attaching anything itself. The pool of free models that accept images is far smaller
        // than the pool overall.
        var requirements = new ChatModelRequirements(
            NeedsImageInput: prompt.CarriesImages,
            MinContextLength: config.OpenRouter.MinContextLength);

        var completion = await CompleteAsync(prompt, requirements, config, now, cancellationToken)
            .ConfigureAwait(false);

        if (completion.IsFailure)
        {
            return completion.Error;
        }

        var citations = CitationResolver.Resolve(completion.Value.Text, prompt.Excerpts, config.MaxImagesInReply);

        var sources = citations.Sources
            .Select(citation => new AiAnswerSource(
                citation.Number, citation.Excerpt.Label, citation.Excerpt.SourceUrl, citation.IsImage))
            .ToList();

        // Attribution cannot rest on the model remembering to cite. Free models comply
        // inconsistently — the same question cites on one run and not the next — so when an answer
        // credits nothing at all, the guides it was given are credited for it.
        //
        // Listed without numbers, because there is no marker in the prose for them to anchor, and
        // labelled as the closest matches rather than as sources: what the model did with the
        // excerpts is unknown, and claiming it drew on them would be inventing a citation rather than
        // supplying a missing one. Chunks of one document share a link, which is credited once.
        if (sources.Count == 0)
        {
            sources = [.. prompt.Excerpts
                .DistinctBy(excerpt => excerpt.SourceUrl, StringComparer.Ordinal)
                .Take(MaxUncitedSourcesInReply)
                .Select(excerpt => new AiAnswerSource(Marker: null, excerpt.Label, excerpt.SourceUrl))];
        }

        var depth = context.ParentDepth + 1;

        return new AiAnswer(
            citations.Text,
            [.. citations.Images.Select(image => new AiAnswerImage(
                image.Number, image.Excerpt.ImageUrl!, image.Excerpt.SourceUrl, image.Excerpt.Title))],
            sources,
            completion.Value.ModelUsed,
            ResolveConversationId(context, request),
            depth,
            depth >= limits.LongThreadDepth,
            // Offer order is hit order, so rank position survives without storing the scores.
            [.. prompt.Excerpts.Select(excerpt => excerpt.SourceUrl).Distinct(StringComparer.Ordinal)],
            // Pictures included: an answer that cited only pictures used to be archived as citing nothing.
            [.. citations.Sources.Select(citation => citation.Excerpt.SourceUrl).Distinct(StringComparer.Ordinal)]);
    }

    /// <summary>
    /// Asks for an answer, and asks different models once more when the first chain fails in a way
    /// other models could fix — or answers with something that is not an answer at all.
    ///
    /// Beta testers were shown "User Safety: safe" as an answer three times: a safety classifier the
    /// provider's random fallback router had picked. It was a perfectly valid completion, so nothing
    /// noticed. Every answer is therefore screened before it is used, and a failed first attempt costs
    /// a second request only when that request can actually reach a model not yet asked.
    /// </summary>
    private async Task<Result<AiChatResponse>> CompleteAsync(
        AiPrompt prompt,
        ChatModelRequirements requirements,
        AiConfiguration config,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var patience = TimeSpan.FromSeconds(config.RequestTimeoutSeconds);
        var tried = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var chain = await modelCatalog.ReadChainAsync(requirements, cancellationToken).ConfigureAwait(false);

        for (var attempt = 1; ; attempt++)
        {
            var started = Stopwatch.GetTimestamp();
            var completion = await chatClient
                .CompleteAsync(new AiChatRequest(chain, prompt.Messages, Temperature: 0.2), cancellationToken)
                .ConfigureAwait(false);
            var elapsed = Stopwatch.GetElapsedTime(started);

            Error failure;
            if (completion.IsSuccess)
            {
                var response = completion.Value;

                // Every completion is billed in tokens, including one about to be discarded.
                await budget.RecordTokenUsageAsync(
                    today,
                    response.Usage.PromptTokens,
                    response.Usage.CompletionTokens,
                    cancellationToken).ConfigureAwait(false);

                // The models the provider skipped before this one failed, whatever is made of the answer.
                modelCatalog.ReportChainOutcome(chain, response.ModelUsed);

                var isVerdict = GuardrailVerdictDetector.IsVerdict(response.Text);
                var isUnfit = modelCatalog.IsUnfitToAnswer(response.ModelUsed);
                if (!isVerdict && !isUnfit)
                {
                    return response;
                }

                if (isVerdict && !isUnfit)
                {
                    // A classifier the description rules did not recognise. Kept out until restart, and
                    // named in the log so it can be blocked for good.
                    modelCatalog.ReportUnfit(response.ModelUsed);
                }

                LogUnusableAnswer(logger, response.ModelUsed, attempt);

                MarkTried(tried, chain, response.ModelUsed);
                failure = Error.Unexpected(ChatErrorCodes.UnusableAnswer,
                    "The AI model I reached couldn't give a real answer just now. Please ask again in a moment.");
            }
            else
            {
                failure = completion.Error;

                if (AnswerAttemptPolicy.BlamesModels(failure))
                {
                    modelCatalog.ReportChainOutcome(chain, answeredBy: null);
                }

                tried.UnionWith(chain);
            }

            if (attempt >= MaxChatAttempts || !AnswerAttemptPolicy.ShouldRetry(failure, elapsed, patience))
            {
                return failure;
            }

            // Read before the retry's request is claimed, so a question that has already tried every
            // model it could costs nothing more.
            chain = await modelCatalog.ReadChainAsync(
                requirements with { ExcludedModelIds = new HashSet<string>(tried, StringComparer.OrdinalIgnoreCase) },
                cancellationToken).ConfigureAwait(false);

            if (chain.Count == 0)
            {
                return failure;
            }

            var reserved = await budget.TryReserveRequestsAsync(
                today,
                RequestsPerRetry,
                config.OpenRouter.DailyRequestBudget,
                cancellationToken).ConfigureAwait(false);

            if (!reserved)
            {
                return failure;
            }

            LogRetrying(logger, failure.Code, string.Join(", ", chain));
        }
    }

    /// <summary>
    /// Records what an unusable answer used up: the chain up to and including the model that gave it.
    /// Entries after it were never reached and stay available to the retry — a chain whose first model
    /// answered with a verdict still has two good models nobody has asked. An answerer the chain does
    /// not name was the router's pick, and then every entry was tried.
    /// </summary>
    private static void MarkTried(HashSet<string> tried, IReadOnlyList<string> chain, string answeredBy)
    {
        var reached = chain.Count;
        for (var index = 0; index < chain.Count; index++)
        {
            if (string.Equals(chain[index], answeredBy, StringComparison.OrdinalIgnoreCase))
            {
                reached = index + 1;
                break;
            }
        }

        tried.UnionWith(chain.Take(reached));
        tried.Add(answeredBy);
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
    ///
    /// The root comes from the parent turn rather than from the message being replied to. Those are
    /// the same thing only on the very first follow-up: past that, using the parent's id re-roots the
    /// tree at every exchange, so a conversation ends up stored as a chain of two-turn fragments and
    /// both the replayed history and the archived transcript stop at the last one.
    /// </summary>
    private static ulong ResolveConversationId(ConversationContext context, AskAiCommand request) =>
        context.ConversationId ?? request.DiscordMessageId;

    [LoggerMessage(LogLevel.Warning,
        "Guide index search failed (text vector: {TextVectorLength}, image vector: {ImageVectorLength}).")]
    static partial void LogSearchFailed(ILogger logger, int textVectorLength, int imageVectorLength, Exception exception);

    [LoggerMessage(LogLevel.Warning,
        "AI model {ModelId} returned something that is not an answer on attempt {Attempt}; discarding it.")]
    static partial void LogUnusableAnswer(ILogger logger, string modelId, int attempt);

    [LoggerMessage(LogLevel.Information, "Retrying an AI answer after {ErrorCode}, now asking {Chain}.")]
    static partial void LogRetrying(ILogger logger, string errorCode, string chain);
}
