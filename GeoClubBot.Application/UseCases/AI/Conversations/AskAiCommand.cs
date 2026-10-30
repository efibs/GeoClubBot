using System.Diagnostics;
using Configuration;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.Abstractions;
using UseCases.OutputPorts.AI;
using UseCases.OutputPorts.Repositories;
using UseCases.UseCases.AI.Retrieval;
using UseCases.UseCases.AI.Tools;
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
/// <param name="Position">
/// Which of its excerpt's attached pictures this is, from one. An excerpt can bring several — one slide
/// caption sits beside each picture of its slide — and every one keeps the excerpt's number. Discord
/// shows pictures sharing a link as one grid under one title, so this only tells their files apart.
/// </param>
/// <param name="Count">How many of its excerpt's pictures are attached.</param>
public sealed record AiAnswerImage(
    int Marker,
    string ImageUrl,
    string SourceUrl,
    string? Title,
    int Position = 1,
    int Count = 1);

/// <param name="Marker">
/// The number the answer's prose cites, so the two line up — or <c>null</c> when the model cited
/// nothing and the guides are being credited on its behalf, where a number would point nowhere.
/// </param>
/// <param name="IsImage">A cited picture, listed alongside the text sources so the numbers stay contiguous.</param>
public sealed record AiAnswerSource(int? Marker, string Label, string Url, bool IsImage = false);

/// <param name="ConversationId">Root message id; the caller stores it on both resulting turns.</param>
/// <param name="IsLongThread">True when the branch is deep enough to suggest starting a fresh one.</param>
/// <param name="RetrievedSourceUrls">
/// Every guide offered to the model in the order it was numbered — not only the ones it used: on a
/// follow-up first the excerpts the previous answer cited, then the retrieved ones, best match first,
/// then what its tool calls offered. Stored rather than only rendered, because a rated-bad answer has
/// two causes that read identically in the text: the right guide was never retrieved, or it was
/// retrieved and the model ignored it.
/// </param>
/// <param name="CitedSourceUrls">The guides the answer actually pointed at, as shown beneath it.</param>
/// <param name="ToolCalls">
/// The tool calls the model made on the way, in order; null or empty when it answered in one call.
/// Kept with the answer so that what it searched for can be read beside what it was rated.
/// </param>
/// <param name="OfferedChunkIds">
/// The index chunks behind every excerpt offered, in the order they were numbered, merged copies
/// included: the exact excerpts, where <paramref name="RetrievedSourceUrls"/> names only their pages.
/// </param>
/// <param name="CitedChunkIds">
/// The chunks behind the excerpts the answer cited, in the order it numbered them: what a follow-up to
/// it is offered again first. Empty when it cited nothing.
/// </param>
public sealed record AiAnswer(
    string Text,
    IReadOnlyList<AiAnswerImage> Images,
    IReadOnlyList<AiAnswerSource> Sources,
    string ModelUsed,
    ulong ConversationId,
    int Depth,
    bool IsLongThread,
    IReadOnlyList<string> RetrievedSourceUrls,
    IReadOnlyList<string> CitedSourceUrls,
    IReadOnlyList<AiToolCallSummary>? ToolCalls = null,
    IReadOnlyList<Guid>? OfferedChunkIds = null,
    IReadOnlyList<Guid>? CitedChunkIds = null);

public sealed partial class AskAiHandler(
    IAiConversationRepository conversations,
    IAiBudgetRepository budget,
    IEmbedder embedder,
    IKnowledgeIndex knowledgeIndex,
    IChatModelCatalog modelCatalog,
    IChatModelClient chatClient,
    AiAnswerLoop answerLoop,
    PlantRangeLookAhead plantRange,
    KnowledgeCountryDirectory countryDirectory,
    QueryPlannerPause plannerPause,
    IOptions<AiConfiguration> aiConfiguration,
    IOptions<AiConversationConfiguration> conversationConfiguration,
    IOptions<AiRetrievalConfiguration> retrievalConfiguration,
    TimeProvider timeProvider,
    ILogger<AskAiHandler> logger)
    : IRequestHandler<AskAiCommand, Result<AiAnswer>>
{
    /// <summary>
    /// Guides credited when the model cited none. These are the best matches found for the question
    /// rather than anything the answer pointed at, so a long list would overstate them.
    /// </summary>
    private const int MaxUncitedSourcesInReply = 3;

    /// <summary>
    /// One question costs two upstream calls: embedding the query, then generating the answer. Both
    /// are claimed up front so a request that would exceed the daily allowance is refused before any
    /// of it is spent. The query planner, a retry and each further call of the tool rounds claim their
    /// own request when they happen, and are skipped rather than the question refused when the
    /// allowance cannot cover them; see <see cref="AiAnswerLoop"/>.
    /// </summary>
    private const int RequestsPerQuestion = 2;

    /// <summary>The query planner's one chat request.</summary>
    private const int RequestsPerPlan = 1;

    public async Task<Result<AiAnswer>> Handle(AskAiCommand request, CancellationToken cancellationToken)
    {
        var config = aiConfiguration.Value;
        var limits = conversationConfiguration.Value;
        var now = DateTimeOffset.UtcNow;
        var startedAt = timeProvider.GetUtcNow();

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
            return AiAnswerErrors.BudgetExhausted;
        }

        // Read while the question is searched: a read by id costs no request, and never fails the question.
        var maxCarried = retrievalConfiguration.Value.MaxCarriedExcerpts;
        var carriedRead = ReadCarriedAsync(context, maxCarried, cancellationToken);

        // Also read while the question is searched: a GBIF lookup costs no request, and never fails it.
        var plantRead = plantRange.ReadAsync(request.Content, cancellationToken);

        // Deliberately not released on later failure: by this point at least one upstream call has
        // usually been made, and over-counting is far safer than a 429 storm from under-counting.
        var hits = await RetrieveAsync(request, context, config, now, cancellationToken).ConfigureAwait(false);
        if (hits.IsFailure)
        {
            // Deliberately not answered anyway. An empty hit list reads to the model as "the guides do
            // not cover this", so a failed lookup would come back as a confident statement that the
            // corpus is silent on something it documents well — worse than no answer, and it spends
            // the chat request to produce it. Retrieval failures here are transient and retryable.
            return hits.Error;
        }

        // A plant's countries lead the excerpts found for the question: they answer its "where", while
        // the guides found beside them cover a country or two (Khasi pines: India and Vietnam only).
        IReadOnlyList<KnowledgeHit> retrieved = await plantRead.ConfigureAwait(false) is { } plant
            ? [plant, .. hits.Value]
            : hits.Value;

        var offered = CarriedExcerpts.Combine(
            await carriedRead.ConfigureAwait(false), retrieved, maxCarried, context.PreviousCitedChunkIds);

        var completion = await answerLoop.AnswerAsync(
                new AnswerRequest(
                    context, request.Content, request.AttachmentImageUrls, offered.Hits,
                    DateOnly.FromDateTime(now.UtcDateTime), startedAt, offered.Carried, offered.CarriedSourceNumbers),
                cancellationToken)
            .ConfigureAwait(false);

        if (completion.IsFailure)
        {
            return completion.Error;
        }

        var outcome = completion.Value;

        // Tool excerpts included: a number the answer cites from a search it made resolves, lists and
        // attaches exactly like one it was given with the question.
        var citations = CitationResolver.Resolve(outcome.Response.Text, outcome.Excerpts, config.MaxImagesInReply);

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
        // supplying a missing one. Chunks of one section of a document are one line, as when cited
        // (CitationResolver.SectionKeyOf).
        //
        // On a follow-up the excerpts the previous answer cited lead the list, but they were not found
        // for this question: credited first, an answer about Indonesia listed the Malaysia guides the
        // answer before it had cited as its closest matches. They are credited only when nothing was
        // found for this question at all.
        if (sources.Count == 0)
        {
            var found = outcome.Excerpts.Where(excerpt => excerpt.Marker > offered.Carried).ToList();

            sources = [.. (found.Count > 0 ? found : outcome.Excerpts)
                .DistinctBy(CitationResolver.SectionKeyOf, StringComparer.Ordinal)
                .Take(MaxUncitedSourcesInReply)
                .Select(excerpt => new AiAnswerSource(Marker: null, excerpt.Label, excerpt.SourceUrl))];
        }

        var depth = context.ParentDepth + 1;

        return new AiAnswer(
            citations.Text,
            // Titled as listed beneath the answer when the source has no title of its own.
            [.. citations.Images.Select(image => new AiAnswerImage(
                image.Number, image.ImageUrl, image.Excerpt.SourceUrl, image.Excerpt.PictureTitle, image.Position, image.Count))],
            sources,
            outcome.Response.ModelUsed,
            ResolveConversationId(context, request),
            depth,
            depth >= limits.LongThreadDepth,
            // Offer order is hit order, so rank position survives without storing the scores. The tools'
            // excerpts follow in the order they were offered.
            [.. outcome.Excerpts.Select(excerpt => excerpt.SourceUrl).Distinct(StringComparer.Ordinal)],
            // Pictures included: an answer that cited only pictures used to be archived as citing nothing.
            [.. citations.Sources.Select(citation => citation.Excerpt.SourceUrl).Distinct(StringComparer.Ordinal)],
            outcome.ToolCalls,
            [.. outcome.Excerpts.SelectMany(excerpt => excerpt.ChunkIds).Distinct()],
            // Only what the prose cites: the guides credited on the model's behalf above are the closest
            // matches, not what a follow-up's "source 2" could mean.
            [.. citations.CitedExcerpts.SelectMany(excerpt => excerpt.ChunkIds).Distinct()]);
    }

    /// <summary>
    /// The excerpts the answer being replied to cited, read back from the index by id, in the order it
    /// numbered them. Empty for a fresh question, when carrying is off, or when the read fails: a
    /// follow-up is then answered from its own search, as before. Excerpts no longer in the index — a
    /// guide re-indexed since, a species range that never was in it — are simply not found.
    /// </summary>
    private async Task<IReadOnlyList<KnowledgeHit>> ReadCarriedAsync(
        ConversationContext context,
        int maxCarried,
        CancellationToken cancellationToken)
    {
        var ids = context.PreviousCitedChunkIds;
        if (maxCarried <= 0 || ids.Count == 0)
        {
            return [];
        }

        try
        {
            IReadOnlyList<KnowledgeHit> carried =
                [.. await knowledgeIndex.ReadChunksAsync(ids, cancellationToken).ConfigureAwait(false) ?? []];

            LogCarried(logger, carried.Count, ids.Count);
            return carried;
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogCarryFailed(logger, ids.Count, exception);
            return [];
        }
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
    /// Returns the retrieved excerpts, or a failure. The distinction matters: "nothing matched" and
    /// "the lookup did not happen" are different answers, and collapsing them into an empty list makes
    /// the model assert the first when the second is true.
    ///
    /// The question is searched by every wording <see cref="RetrievalQueryComposer"/> gives it — its
    /// branch, the planner's rewrite and phrasings — all embedded in one request, so a follow-up costs
    /// no more embedding than a fresh question.
    /// </summary>
    private async Task<Result<IReadOnlyList<KnowledgeHit>>> RetrieveAsync(
        AskAiCommand request,
        ConversationContext context,
        AiConfiguration config,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var settings = retrievalConfiguration.Value;
        var hasScreenshot = request.AttachmentImageUrls.Count > 0;

        // Read while the planner thinks: the first question after a start-up scrolls the whole index
        // for its country names. Never fails; a failed read only means no country is boosted.
        var gazetteerRead = settings.CountryBoost.Enabled
            ? countryDirectory.ReadAsync(cancellationToken)
            : Task.FromResult(CountryGazetteer.Empty);

        var plan = await PlanAsync(request, context, settings, config, now, cancellationToken).ConfigureAwait(false);
        var gazetteer = await gazetteerRead.ConfigureAwait(false);

        var searchPlan = RetrievalQueryComposer.Compose(
            request.Content, context.Turns, hasScreenshot, plan, gazetteer, settings);

        List<EmbeddingInput> inputs = [.. searchPlan.Texts.Select(text => new TextEmbeddingInput(text))];
        if (hasScreenshot)
        {
            inputs.Add(new ImageEmbeddingInput(request.AttachmentImageUrls[0]));
        }

        var embeddings = await embedder.EmbedAsync(inputs, cancellationToken).ConfigureAwait(false);
        if (embeddings.IsFailure)
        {
            return embeddings.Error;
        }

        var textCount = searchPlan.Texts.Count;

        // Written out rather than folded into the initialiser. ReadOnlyMemory<float> converts
        // implicitly from an array, and the null literal converts to an array, so a conditional
        // expression here takes ReadOnlyMemory<float> as its natural type and the null branch becomes
        // an *empty* memory rather than no value at all. The store then rejects the whole query with
        // "expected dim: 2048, got 0" — a text-only question failing on the image vector it never had.
        ReadOnlyMemory<float>? imageVector = null;
        if (embeddings.Value.Count > textCount)
        {
            imageVector = embeddings.Value[textCount];
        }

        var query = searchPlan.ToQuery([.. embeddings.Value.Take(textCount)], imageVector, settings);

        LogRetrieving(logger, searchPlan.Lead.Name, searchPlan.Others.Count,
            string.Join(", ", searchPlan.Countries.Select(country => country.Name)), searchPlan.KeyPhrases.Count);

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
    /// Asks a model how to search for the question: a standalone rewrite, phrasings in the guides' own
    /// terms, and the countries it is about. Returns null — and the question is searched by its own
    /// words and its branch — whenever that is not to be had cheaply: the planner is off or sitting out
    /// a pause after a timeout, there is no model to ask, the daily allowance cannot cover one more
    /// request, the call fails or outlasts its timeout, or its reply cannot be read. Planning is an
    /// improvement to a question, never a reason for it to fail.
    /// </summary>
    private async Task<RetrievalPlan?> PlanAsync(
        AskAiCommand request,
        ConversationContext context,
        AiRetrievalConfiguration settings,
        AiConfiguration config,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var planner = settings.QueryPlanner;
        if (!planner.Enabled)
        {
            return null;
        }

        if (plannerPause.ResumesAt is { } resumesAt)
        {
            LogPlannerPaused(logger, resumesAt);
            return null;
        }

        // Text only, whatever the question carries: the planner rewrites words, and the pool of free
        // models that read pictures is far smaller.
        var chain = await modelCatalog.ReadChainAsync(
            new ChatModelRequirements(MinContextLength: config.OpenRouter.MinContextLength),
            cancellationToken).ConfigureAwait(false);

        if (chain.Count == 0)
        {
            return null;
        }

        // Claimed against a share of the allowance, as indexing is: past it, the requests left go to
        // answers rather than to making them better. At least one, or a small allowance would never plan.
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var plannerCeiling = Math.Max(
            1, config.OpenRouter.DailyRequestBudget * Math.Clamp(planner.MaxDailyBudgetPercent, 1, 100) / 100);
        var reserved = await budget.TryReserveRequestsAsync(
            today, RequestsPerPlan, plannerCeiling, cancellationToken).ConfigureAwait(false);

        if (!reserved)
        {
            LogPlannerSkippedForBudget(logger);
            return null;
        }

        var messages = RetrievalQueryPlanner.BuildMessages(
            context.Turns, request.Content, request.AttachmentImageUrls.Count > 0);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(planner.TimeoutSeconds));

        var started = Stopwatch.GetTimestamp();
        Result<AiChatResponse> completion;
        try
        {
            completion = await chatClient
                .CompleteAsync(new AiChatRequest(chain, messages, Temperature: RetrievalQueryPlanner.Temperature), timeout.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            PauseAfterTimeout(planner);
            return null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The client reports what it expects as results; anything else it lets through would fail
            // the answer the plan was only meant to improve.
            LogPlannerThrew(logger, exception);
            return null;
        }

        var elapsed = Stopwatch.GetElapsedTime(started);

        if (completion.IsFailure)
        {
            // The client reports a cancelled request as an unreachable provider. When it was the
            // planner's own patience that ran out, that is what the log says, and no model is blamed.
            if (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                PauseAfterTimeout(planner);
                return null;
            }

            // Blamed as the answer's chain would be, so the answer that follows skips models that
            // just failed. A timeout of the planner's own is no model's fault and is not blamed.
            modelCatalog.ReportFailedRequest(chain, completion.Error);

            LogPlannerFailed(logger, completion.Error.Code, elapsed.TotalSeconds);
            return null;
        }

        var response = completion.Value;
        await budget.RecordTokenUsageAsync(
            today, response.Usage.PromptTokens, response.Usage.CompletionTokens, cancellationToken).ConfigureAwait(false);
        modelCatalog.ReportChainOutcome(chain, response.ModelUsed);

        var isVerdict = GuardrailVerdictDetector.IsVerdict(response.Text);
        var isUnfit = modelCatalog.IsUnfitToAnswer(response.ModelUsed);
        if (isVerdict && !isUnfit)
        {
            // As for an answer: a classifier the description rules missed is kept out of the answer's
            // chain too, which would otherwise ask it next.
            modelCatalog.ReportUnfit(response.ModelUsed);
        }

        var plan = isVerdict || isUnfit ? null : RetrievalQueryPlanner.Parse(response.Text, planner.MaxPhrasings);

        if (plan is null)
        {
            LogPlannerUnreadable(logger, response.ModelUsed, elapsed.TotalSeconds);
            return null;
        }

        // How long it took is worth seeing at a glance — it decides whether planning pays — but the
        // rewrite is the user's question in other words, so it stays at debug level.
        LogPlanned(logger, response.ModelUsed, elapsed.TotalSeconds, plan.SearchPhrasings.Count, string.Join(", ", plan.Countries));
        LogPlannedQuestion(logger, plan.StandaloneQuestion ?? "(none)", string.Join(" | ", plan.SearchPhrasings));
        return plan;
    }

    /// <summary>
    /// The planner ran out of time. A slow model at the head of the chain would make every question
    /// wait as long, so it sits out a while; see <see cref="QueryPlannerPause"/>.
    /// </summary>
    private void PauseAfterTimeout(AiQueryPlannerConfiguration planner)
    {
        plannerPause.PauseFor(TimeSpan.FromMinutes(planner.PauseAfterTimeoutMinutes));
        LogPlannerTimedOut(logger, planner.TimeoutSeconds, planner.PauseAfterTimeoutMinutes);
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

    [LoggerMessage(LogLevel.Debug,
        "Offering a follow-up again {ExcerptCount} excerpt(s) the answer it replies to cited ({ChunkCount} chunk id(s) asked for).")]
    static partial void LogCarried(ILogger logger, int excerptCount, int chunkCount);

    [LoggerMessage(LogLevel.Warning,
        "Reading back the {ChunkCount} chunk(s) the previous answer cited failed; the follow-up is answered from its own search.")]
    static partial void LogCarryFailed(ILogger logger, int chunkCount, Exception exception);

    [LoggerMessage(LogLevel.Warning,
        "Guide index search failed (text vector: {TextVectorLength}, image vector: {ImageVectorLength}).")]
    static partial void LogSearchFailed(ILogger logger, int textVectorLength, int imageVectorLength, Exception exception);

    [LoggerMessage(LogLevel.Debug,
        "Searching the guides by the {Lead} wording and {OtherCount} more, countries [{Countries}], {PhraseCount} key phrase(s).")]
    static partial void LogRetrieving(ILogger logger, string lead, int otherCount, string countries, int phraseCount);

    [LoggerMessage(LogLevel.Information,
        "Query planner ({ModelId}, {Seconds:0.0} s) planned the search: {PhrasingCount} phrasing(s), countries [{Countries}].")]
    static partial void LogPlanned(ILogger logger, string modelId, double seconds, int phrasingCount, string countries);

    [LoggerMessage(LogLevel.Debug, "Query planner rewrote the question as \"{Question}\", phrasings: {Phrasings}.")]
    static partial void LogPlannedQuestion(ILogger logger, string question, string phrasings);

    [LoggerMessage(LogLevel.Information,
        "Query planner skipped: today's requests have reached its share of the allowance. Searching by the question and its branch.")]
    static partial void LogPlannerSkippedForBudget(ILogger logger);

    [LoggerMessage(LogLevel.Information,
        "Query planner gave no answer within {TimeoutSeconds} s. Searching by the question and its branch, and skipping the planner for {PauseMinutes} min.")]
    static partial void LogPlannerTimedOut(ILogger logger, int timeoutSeconds, int pauseMinutes);

    [LoggerMessage(LogLevel.Debug,
        "Query planner paused after a timeout until {ResumesAt:u}. Searching by the question and its branch.")]
    static partial void LogPlannerPaused(ILogger logger, DateTimeOffset resumesAt);

    [LoggerMessage(LogLevel.Information,
        "Query planner failed with {ErrorCode} after {Seconds:0.0} s. Searching by the question and its branch.")]
    static partial void LogPlannerFailed(ILogger logger, string errorCode, double seconds);

    [LoggerMessage(LogLevel.Warning, "Query planner failed unexpectedly. Searching by the question and its branch.")]
    static partial void LogPlannerThrew(ILogger logger, Exception exception);

    [LoggerMessage(LogLevel.Information,
        "Query planner reply from {ModelId} ({Seconds:0.0} s) could not be read. Searching by the question and its branch.")]
    static partial void LogPlannerUnreadable(ILogger logger, string modelId, double seconds);
}
