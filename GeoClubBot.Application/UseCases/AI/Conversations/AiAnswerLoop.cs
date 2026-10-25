using System.Diagnostics;
using Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.OutputPorts.AI;
using UseCases.OutputPorts.Repositories;
using UseCases.UseCases.AI.Tools;
using Utilities;

namespace UseCases.UseCases.AI.Conversations;

/// <param name="Context">The branch the question continues.</param>
/// <param name="Question">The message being answered.</param>
/// <param name="AttachmentImageUrls">The pictures it carries.</param>
/// <param name="Hits">The excerpts offered with it: those carried from the previous answer, then those retrieved, best first.</param>
/// <param name="BudgetDay">The day its requests count against, as its first two were claimed.</param>
/// <param name="StartedAt">When it arrived: <see cref="AiToolsConfiguration.AnswerDeadlineSeconds"/> counts from here.</param>
/// <param name="CarriedCount">How many of <paramref name="Hits"/>, from the first, the previous answer cited (<see cref="CarriedExcerpts"/>).</param>
public sealed record AnswerRequest(
    ConversationContext Context,
    string Question,
    IReadOnlyList<string> AttachmentImageUrls,
    IReadOnlyList<KnowledgeHit> Hits,
    DateOnly BudgetDay,
    DateTimeOffset StartedAt,
    int CarriedCount = 0);

/// <param name="Response">The completion that answered.</param>
/// <param name="Excerpts">Every excerpt offered, the retrieved ones first: citations resolve against these.</param>
/// <param name="ToolCalls">Each tool call made, in order; empty when the model answered straight away.</param>
public sealed record AnswerOutcome(
    AiChatResponse Response,
    IReadOnlyList<OfferedExcerpt> Excerpts,
    IReadOnlyList<AiToolCallSummary> ToolCalls);

/// <summary>Errors the answer reports in the bot's own words.</summary>
public static class AiAnswerErrors
{
    public const string BudgetExhaustedCode = "ai.budget_exhausted";

    public static Error BudgetExhausted { get; } = Error.Conflict(BudgetExhaustedCode,
        "I'm out of free AI requests for today. They reset at 00:00 UTC.");
}

/// <summary>
/// Gets the answer to a question from the models: one call when that is enough, and tool calls when
/// the model asks for them.
///
/// The first call is the one the bot always made — the question with its retrieved excerpts — with
/// the tools declared beside it. A model that answers straight away has cost exactly what a question
/// always cost. A model that asks for tools gets them run, their results appended, and is asked again,
/// until it answers, its rounds or the answer's deadline are used up, or the day's allowance runs short;
/// then the next call has to answer. Tools are only offered on a chain built for them: never the
/// fallback router, which picks a model at random, and never when the allowance cannot pay for at
/// least one round and the call that answers from it. Without either, the question is answered in one
/// call, the way it always was.
///
/// The first call keeps its one retry against untried models (<see cref="AnswerAttemptPolicy"/>). Once
/// a model has answered, it heads the chain for the rest, because a tool call's id is its provider's.
/// A later call that fails does not lose what was gathered: one last call without tools is asked with
/// every excerpt in the prompt, as a single-shot question would have been. Only the text that answers
/// is screened for a classifier's verdict — a tool call is never one — and an answer is never empty.
/// </summary>
public sealed partial class AiAnswerLoop(
    IChatModelClient chatClient,
    IChatModelCatalog modelCatalog,
    IAiBudgetRepository budget,
    AnswerToolbox toolbox,
    IOptions<AiConfiguration> aiConfiguration,
    IOptions<AiToolsConfiguration> toolsConfiguration,
    TimeProvider timeProvider,
    ILogger<AiAnswerLoop> logger)
{
    private const double Temperature = 0.2;

    /// <summary>
    /// A first chain that fails, or answers with something that is not an answer, gets one more try
    /// against models not yet asked. One, because each costs a request from the daily allowance and a
    /// provider failing twice in a row is rarely going to answer a third time.
    /// </summary>
    private const int MaxChatAttempts = 2;

    private const int RequestsPerCall = 1;

    /// <summary>A round's searches share one embedding request, however many there are.</summary>
    private const int RequestsPerSearchRound = 1;

    /// <summary>
    /// What tools have to be able to cost before they are offered: one round's embedding and the call
    /// that answers from it. Offering them when the allowance cannot pay that far would only leave the
    /// model asking for a search nobody can afford to run.
    /// </summary>
    private const int RequestsToUseTools = RequestsPerSearchRound + RequestsPerCall;

    public async Task<Result<AnswerOutcome>> AnswerAsync(AnswerRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var config = aiConfiguration.Value;
        var tools = toolsConfiguration.Value;

        var prompt = AiPromptBuilder.Build(
            request.Context, request.Question, request.AttachmentImageUrls, request.Hits, request.CarriedCount);

        // A vision-capable model only when a picture actually travels with the request — the question's
        // own attachment, or a screenshot replayed from earlier in the branch, which a follow-up sends
        // without attaching anything itself. The pool of free models that accept images is far smaller
        // than the pool overall.
        var requirements = new ChatModelRequirements(
            NeedsImageInput: prompt.CarriesImages,
            MinContextLength: config.OpenRouter.MinContextLength);
        var singleShot = new Asking(prompt.Messages, requirements, Tools: null);

        var toolChain = tools.Enabled
            ? await ReadToolChainAsync(requirements with { NeedsTools = true }, excluded: null, config, cancellationToken).ConfigureAwait(false)
            : [];

        if (toolChain.Count == 0
            || !await CanAffordToolRoundAsync(request.BudgetDay, config, cancellationToken).ConfigureAwait(false))
        {
            if (tools.Enabled)
            {
                LogAnsweringWithoutTools(logger, toolChain.Count == 0 ? "no model in the chain can call tools" : "the day's allowance is short");
            }

            var chain = await modelCatalog.ReadChainAsync(requirements, cancellationToken).ConfigureAwait(false);
            var single = await CompleteFirstAsync(singleShot, chain, withoutTools: null, request.BudgetDay, config, cancellationToken)
                .ConfigureAwait(false);

            return single.IsFailure ? single.Error : new AnswerOutcome(single.Value.Response, prompt.Excerpts, []);
        }

        var definitions = AnswerTools.Definitions(tools.SpeciesRange.Enabled);
        var toolPrompt = AiPromptBuilder.AppendToSystemPrompt(prompt, AnswerTools.SystemPromptFor(tools.SpeciesRange.Enabled));
        var withTools = new Asking(toolPrompt.Messages, requirements with { NeedsTools = true }, definitions);

        var first = await CompleteFirstAsync(withTools, toolChain, singleShot, request.BudgetDay, config, cancellationToken)
            .ConfigureAwait(false);

        if (first.IsFailure)
        {
            return first.Error;
        }

        if (!first.Value.OfferedTools || !first.Value.Response.RequestsTools)
        {
            return new AnswerOutcome(first.Value.Response, prompt.Excerpts, []);
        }

        return await RunToolRoundsAsync(
                request, requirements, toolPrompt.Messages, definitions, first.Value.Response, first.Value.Chain, config, tools, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Runs the tool calls the model asked for and asks it again, until it answers. The call that will
    /// answer from a round is claimed before the round runs — a question must never gather what it
    /// cannot then answer from — and the round's embedding after it, so a search that cannot be paid
    /// for is the only thing given up.
    /// </summary>
    private async Task<Result<AnswerOutcome>> RunToolRoundsAsync(
        AnswerRequest request,
        ChatModelRequirements requirements,
        IReadOnlyList<AiChatMessage> toolMessages,
        IReadOnlyList<AiToolDefinition> definitions,
        AiChatResponse response,
        IReadOnlyList<string> chain,
        AiConfiguration config,
        AiToolsConfiguration tools,
        CancellationToken cancellationToken)
    {
        var ledger = new ExcerptLedger(request.Hits);
        var conversation = new List<AiChatMessage>(toolMessages);
        var summaries = new List<AiToolCallSummary>();
        var deadline = request.StartedAt + TimeSpan.FromSeconds(tools.AnswerDeadlineSeconds);
        var choice = AiToolChoice.Auto;
        var round = 0;

        chain = Pin(chain, response.ModelUsed);

        while (true)
        {
            if (!response.RequestsTools)
            {
                if (IsUsable(response, out var unusable))
                {
                    return new AnswerOutcome(response, ledger.Offered, summaries);
                }

                return await RescueAsync(request, requirements, ledger, summaries, unusable, [response.ModelUsed], config, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (choice == AiToolChoice.None)
            {
                // Told to answer, it asked for tools again. Asked once more with nothing to call.
                LogToolsAfterAnswerNow(logger, response.ModelUsed, round);
                return await RescueAsync(request, requirements, ledger, summaries,
                        Error.Unexpected(ChatErrorCodes.EmptyResponse, "The AI model asked for tools instead of answering."), [],
                        config, cancellationToken)
                    .ConfigureAwait(false);
            }

            round++;

            if (!await TryReserveAsync(request.BudgetDay, RequestsPerCall, config, cancellationToken).ConfigureAwait(false))
            {
                LogBudgetRanOutMidAnswer(logger, round);
                return AiAnswerErrors.BudgetExhausted;
            }

            var plan = toolbox.Plan(response.ToolCalls!, round, ledger);
            var searchAllowed = !plan.Searches
                                || await TryReserveAsync(request.BudgetDay, RequestsPerSearchRound, config, cancellationToken).ConfigureAwait(false);
            var executed = await toolbox.RunAsync(plan, ledger, searchAllowed, cancellationToken).ConfigureAwait(false);
            summaries.AddRange(executed.Summaries);

            conversation.Add(AiChatMessage.AssistantToolCalls(response.Text, executed.Calls));
            for (var index = 0; index < executed.Calls.Count; index++)
            {
                conversation.Add(AiChatMessage.ToolResult(executed.Calls[index].Id, executed.Results[index]));
            }

            choice = await ChooseNextAsync(round, deadline, request.BudgetDay, config, tools, cancellationToken).ConfigureAwait(false);

            var started = Stopwatch.GetTimestamp();
            var next = await chatClient
                .CompleteAsync(new AiChatRequest(chain, conversation, Temperature, Tools: definitions, ToolChoice: choice), cancellationToken)
                .ConfigureAwait(false);
            var elapsed = Stopwatch.GetElapsedTime(started);

            if (next.IsFailure)
            {
                modelCatalog.ReportFailedRequest(chain, next.Error);
                LogRoundFailed(logger, round, next.Error.Code, elapsed.TotalSeconds);
                return await RescueAsync(request, requirements, ledger, summaries, next.Error, Blamed(chain, next.Error),
                        config, cancellationToken)
                    .ConfigureAwait(false);
            }

            response = next.Value;
            await RecordAsync(request.BudgetDay, chain, response, cancellationToken).ConfigureAwait(false);
            chain = Pin(chain, response.ModelUsed);
        }
    }

    /// <summary>
    /// Whether the call after a round may still call tools: rounds left, time left, and an allowance
    /// that could pay for another round. Otherwise it has to answer, with the tools still declared — a
    /// conversation holding tool calls is one some providers refuse once none are.
    /// </summary>
    private async Task<AiToolChoice> ChooseNextAsync(
        int round,
        DateTimeOffset deadline,
        DateOnly day,
        AiConfiguration config,
        AiToolsConfiguration tools,
        CancellationToken cancellationToken)
    {
        string? reason = null;
        if (round >= tools.MaxToolRounds)
        {
            reason = "its tool rounds are used up";
        }
        else if (timeProvider.GetUtcNow() >= deadline)
        {
            reason = "its deadline has passed";
        }
        else if (!await CanAffordToolRoundAsync(day, config, cancellationToken).ConfigureAwait(false))
        {
            reason = "the day's allowance is short";
        }

        if (reason is null)
        {
            return AiToolChoice.Auto;
        }

        LogAnswerNow(logger, round, reason);
        return AiToolChoice.None;
    }

    /// <summary>
    /// One last call when a later one failed or did not answer: the question asked the way a
    /// single-shot question is, with every excerpt gathered so far in its prompt and no tool to call.
    /// Not tried when nothing could answer it — the provider out of reach, or throttling our key — nor
    /// when the day's allowance cannot pay for it.
    /// </summary>
    private async Task<Result<AnswerOutcome>> RescueAsync(
        AnswerRequest request,
        ChatModelRequirements requirements,
        ExcerptLedger ledger,
        IReadOnlyList<AiToolCallSummary> summaries,
        Error failure,
        IEnumerable<string> excluded,
        AiConfiguration config,
        CancellationToken cancellationToken)
    {
        if (failure.Code is ChatErrorCodes.RateLimited or ChatErrorCodes.Unreachable or ChatErrorCodes.NoModelAvailable)
        {
            return failure;
        }

        var exclude = new HashSet<string>(excluded, StringComparer.OrdinalIgnoreCase);
        var chain = await modelCatalog
            .ReadChainAsync(exclude.Count > 0 ? requirements with { ExcludedModelIds = exclude } : requirements, cancellationToken)
            .ConfigureAwait(false);

        if (chain.Count == 0)
        {
            return failure;
        }

        if (!await TryReserveAsync(request.BudgetDay, RequestsPerCall, config, cancellationToken).ConfigureAwait(false))
        {
            LogBudgetRanOutMidAnswer(logger, summaries.Count == 0 ? 0 : summaries[^1].Round);
            return AiAnswerErrors.BudgetExhausted;
        }

        LogRescuing(logger, failure.Code ?? "(none)", ledger.Count, string.Join(", ", chain));

        var everything = AiPromptBuilder.Build(
            request.Context, request.Question, request.AttachmentImageUrls, ledger.Hits, request.CarriedCount);
        var completion = await chatClient
            .CompleteAsync(new AiChatRequest(chain, everything.Messages, Temperature), cancellationToken)
            .ConfigureAwait(false);

        if (completion.IsFailure)
        {
            modelCatalog.ReportFailedRequest(chain, completion.Error);
            return completion.Error;
        }

        var response = completion.Value;
        await RecordAsync(request.BudgetDay, chain, response, cancellationToken).ConfigureAwait(false);

        return IsUsable(response, out var unusable)
            ? new AnswerOutcome(response, ledger.Offered, summaries)
            : unusable;
    }

    /// <summary>
    /// Asks for an answer, and asks different models once more when the first chain fails in a way
    /// other models could fix — or answers with something that is not an answer at all.
    ///
    /// Beta testers were shown "User Safety: safe" as an answer three times: a safety classifier the
    /// provider's random fallback router had picked. It was a perfectly valid completion, so nothing
    /// noticed. Every answer is therefore screened before it is used, and a failed first attempt costs
    /// a second request only when that request can actually reach a model not yet asked.
    ///
    /// Asked with tools, the retry asks with tools too, of tool-capable models not yet tried; when none
    /// is left it asks <paramref name="withoutTools"/> of the ordinary chain, router included as always.
    /// A provider that refuses the tools themselves is asked again without them, of the same models.
    /// </summary>
    private async Task<Result<FirstAnswer>> CompleteFirstAsync(
        Asking asking,
        IReadOnlyList<string> chain,
        Asking? withoutTools,
        DateOnly day,
        AiConfiguration config,
        CancellationToken cancellationToken)
    {
        var patience = TimeSpan.FromSeconds(config.RequestTimeoutSeconds);
        var tried = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var attempt = 1; ; attempt++)
        {
            var started = Stopwatch.GetTimestamp();
            var completion = await chatClient
                .CompleteAsync(new AiChatRequest(chain, asking.Messages, Temperature, Tools: asking.Tools), cancellationToken)
                .ConfigureAwait(false);
            var elapsed = Stopwatch.GetElapsedTime(started);

            Error failure;
            if (completion.IsSuccess)
            {
                var response = completion.Value;
                await RecordAsync(day, chain, response, cancellationToken).ConfigureAwait(false);

                // Tool calls are never a classifier's verdict, so they are not screened.
                if (asking.Tools is not null && response.RequestsTools)
                {
                    return new FirstAnswer(response, chain, OfferedTools: true);
                }

                if (IsUsable(response, out failure))
                {
                    return new FirstAnswer(response, chain, OfferedTools: asking.Tools is not null);
                }

                LogUnusableAnswer(logger, response.ModelUsed, attempt);
                MarkTried(tried, chain, response.ModelUsed);
            }
            else
            {
                failure = completion.Error;
                modelCatalog.ReportFailedRequest(chain, failure);
                tried.UnionWith(chain);
            }

            var dropTools = asking.Tools is not null && withoutTools is not null && failure.Code == ChatErrorCodes.ToolsUnsupported;
            if (attempt >= MaxChatAttempts || (!dropTools && !AnswerAttemptPolicy.ShouldRetry(failure, elapsed, patience)))
            {
                return failure;
            }

            // Read before the retry's request is claimed, so a question that has already tried every
            // model it could costs nothing more.
            IReadOnlyList<string> next = [];
            if (dropTools)
            {
                // The models were not at fault, so they are asked again — without the tools.
                LogToolsRefused(logger, string.Join(", ", chain));
                asking = withoutTools!;
                next = await modelCatalog.ReadChainAsync(asking.Requirements, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                if (asking.Tools is not null)
                {
                    next = await ReadToolChainAsync(asking.Requirements, tried, config, cancellationToken).ConfigureAwait(false);
                    if (next.Count == 0 && withoutTools is not null)
                    {
                        asking = withoutTools;
                    }
                }

                if (next.Count == 0)
                {
                    next = await modelCatalog.ReadChainAsync(
                        asking.Requirements with { ExcludedModelIds = new HashSet<string>(tried, StringComparer.OrdinalIgnoreCase) },
                        cancellationToken).ConfigureAwait(false);
                }
            }

            if (next.Count == 0)
            {
                return failure;
            }

            if (!await TryReserveAsync(day, RequestsPerCall, config, cancellationToken).ConfigureAwait(false))
            {
                return failure;
            }

            LogRetrying(logger, failure.Code ?? "(none)", string.Join(", ", next));
            chain = next;
        }
    }

    /// <summary>
    /// The chain for a request with tools: models that can call them, without the fallback router. The
    /// router picks a free model at random, and nothing says the one it picks can call a tool.
    /// </summary>
    private async Task<IReadOnlyList<string>> ReadToolChainAsync(
        ChatModelRequirements requirements,
        IReadOnlySet<string>? excluded,
        AiConfiguration config,
        CancellationToken cancellationToken)
    {
        var chain = await modelCatalog.ReadChainAsync(
                excluded is { Count: > 0 }
                    ? requirements with { ExcludedModelIds = new HashSet<string>(excluded, StringComparer.OrdinalIgnoreCase) }
                    : requirements,
                cancellationToken)
            .ConfigureAwait(false);

        return [.. chain.Where(model => !string.Equals(model, config.OpenRouter.FallbackModelId, StringComparison.OrdinalIgnoreCase))];
    }

    /// <summary>
    /// Whether the day's allowance holds what tools could cost. Read, not claimed: each request is
    /// claimed when it is about to be made, and a claim that then fails only ends the tool calls early.
    /// </summary>
    private async Task<bool> CanAffordToolRoundAsync(DateOnly day, AiConfiguration config, CancellationToken cancellationToken)
    {
        var spent = await budget.ReadAsync(day, cancellationToken).ConfigureAwait(false);
        return config.OpenRouter.DailyRequestBudget - (spent?.RequestCount ?? 0) >= RequestsToUseTools;
    }

    private Task<bool> TryReserveAsync(DateOnly day, int requests, AiConfiguration config, CancellationToken cancellationToken) =>
        budget.TryReserveRequestsAsync(day, requests, config.OpenRouter.DailyRequestBudget, cancellationToken);

    /// <summary>
    /// Every completion is billed in tokens, including one about to be discarded, and the models the
    /// provider skipped before the one that answered failed, whatever is made of the answer.
    /// </summary>
    private async Task RecordAsync(DateOnly day, IReadOnlyList<string> chain, AiChatResponse response, CancellationToken cancellationToken)
    {
        await budget.RecordTokenUsageAsync(day, response.Usage.PromptTokens, response.Usage.CompletionTokens, cancellationToken)
            .ConfigureAwait(false);
        modelCatalog.ReportChainOutcome(chain, response.ModelUsed);
    }

    /// <summary>
    /// Whether a completion is an answer: some text, not a classifier's verdict, and not from a model
    /// that should never have been asked. A classifier the description rules did not recognise is kept
    /// out until restart, and named in the log so it can be blocked for good.
    /// </summary>
    private bool IsUsable(AiChatResponse response, out Error failure)
    {
        if (string.IsNullOrWhiteSpace(response.Text))
        {
            failure = Error.Unexpected(ChatErrorCodes.EmptyResponse, "The AI model returned an empty response.");
            return false;
        }

        var isVerdict = GuardrailVerdictDetector.IsVerdict(response.Text);
        var isUnfit = modelCatalog.IsUnfitToAnswer(response.ModelUsed);
        if (!isVerdict && !isUnfit)
        {
            failure = Error.None;
            return true;
        }

        if (isVerdict && !isUnfit)
        {
            modelCatalog.ReportUnfit(response.ModelUsed);
        }

        failure = Error.Unexpected(ChatErrorCodes.UnusableAnswer,
            "The AI model I reached couldn't give a real answer just now. Please ask again in a moment.");
        return false;
    }

    /// <summary>The chain with the model that answered at its head, for the calls that follow.</summary>
    private static IReadOnlyList<string> Pin(IReadOnlyList<string> chain, string answeredBy) =>
        [.. new[] { answeredBy }
            .Concat(chain.Where(model => !string.Equals(model, answeredBy, StringComparison.OrdinalIgnoreCase)))
            .Take(Math.Max(1, chain.Count))];

    /// <summary>The models a failure blames, which the last call is asked without.</summary>
    private static IReadOnlyList<string> Blamed(IReadOnlyList<string> chain, Error failure) =>
        AnswerAttemptPolicy.AssignBlame(failure) switch
        {
            AnswerFailureBlame.WholeChain => chain,
            AnswerFailureBlame.FirstModel or AnswerFailureBlame.FirstModelUnfit when chain.Count > 0 => [chain[0]],
            _ => []
        };

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

    /// <summary>One way of asking: the messages, what they need of a model, and the tools declared, if any.</summary>
    private sealed record Asking(
        IReadOnlyList<AiChatMessage> Messages,
        ChatModelRequirements Requirements,
        IReadOnlyList<AiToolDefinition>? Tools);

    /// <param name="Chain">The chain that answered.</param>
    /// <param name="OfferedTools">The answering call declared tools.</param>
    private sealed record FirstAnswer(AiChatResponse Response, IReadOnlyList<string> Chain, bool OfferedTools);

    [LoggerMessage(LogLevel.Warning,
        "AI model {ModelId} returned something that is not an answer on attempt {Attempt}; discarding it.")]
    static partial void LogUnusableAnswer(ILogger logger, string modelId, int attempt);

    [LoggerMessage(LogLevel.Information, "Retrying an AI answer after {ErrorCode}, now asking {Chain}.")]
    static partial void LogRetrying(ILogger logger, string errorCode, string chain);

    [LoggerMessage(LogLevel.Debug, "Answering without tools: {Reason}.")]
    static partial void LogAnsweringWithoutTools(ILogger logger, string reason);

    [LoggerMessage(LogLevel.Information, "The AI provider refused the tools for {Chain}; asking again without them.")]
    static partial void LogToolsRefused(ILogger logger, string chain);

    [LoggerMessage(LogLevel.Information, "After tool round {Round} the AI model has to answer: {Reason}.")]
    static partial void LogAnswerNow(ILogger logger, int round, string reason);

    [LoggerMessage(LogLevel.Warning, "AI model {ModelId} asked for tools after round {Round}, when it was told to answer.")]
    static partial void LogToolsAfterAnswerNow(ILogger logger, string modelId, int round);

    [LoggerMessage(LogLevel.Warning, "The AI call after tool round {Round} failed with {ErrorCode} after {Seconds:0.0} s.")]
    static partial void LogRoundFailed(ILogger logger, int round, string errorCode, double seconds);

    [LoggerMessage(LogLevel.Information,
        "Asking for the AI answer once more after {ErrorCode}, without tools and with all {ExcerptCount} excerpts gathered, of {Chain}.")]
    static partial void LogRescuing(ILogger logger, string errorCode, int excerptCount, string chain);

    [LoggerMessage(LogLevel.Warning, "The day's AI request allowance ran out during tool round {Round}; no answer was given.")]
    static partial void LogBudgetRanOutMidAnswer(ILogger logger, int round);
}
