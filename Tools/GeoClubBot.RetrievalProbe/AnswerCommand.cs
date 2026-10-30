using System.Diagnostics;
using System.Net.Http.Headers;
using Configuration;
using Infrastructure.OutputAdapters.AI.Gbif;
using Infrastructure.OutputAdapters.AI.OpenRouter;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.OutputPorts.AI;
using UseCases.UseCases.AI.Conversations;
using UseCases.UseCases.AI.Retrieval;
using UseCases.UseCases.AI.Tools;

namespace GeoClubBot.RetrievalProbe;

/// <summary>
/// Asks every rated answer of an export again through the bot's own answering code — <see cref="AskAiHandler"/>
/// with its real collaborators: the embedder, the index (read-only), the chat client and model catalog
/// with today's roster, the tool loop and the GBIF reader — and reports the new answer, as Discord would
/// show it, beside the one the member rated. With <c>--judge</c> a model compares the two.
///
/// What is not the bot's: the conversation store (one branch, rebuilt from the export), the allowance
/// (one that never runs out, fresh for each answer), and the HTTP pipeline, whose pacing and retries
/// <see cref="PacedRetryHandler"/> stands in for. Chat requests are the bot's real cost, so the command
/// needs <c>--allow-chat</c>, and every answer is cached by record, settings and the bot's build.
/// </summary>
public static class AnswerCommand
{
    private const string CacheDirectoryName = "answer-cache";

    public static async Task RunAsync(ProbeContext context)
    {
        var arguments = context.Arguments;
        if (!arguments.AllowChat)
        {
            throw new ProbeException(
                "answer asks the bot's models, a few chat requests per rated answer, so it has to be allowed by name: pass --allow-chat. "
                + "Answers are cached, so a rerun asks only what is not.");
        }

        if (context.Settings.ApiKey is not { } apiKey)
        {
            throw new ProbeException(ProbeSettings.MissingKeyHelp);
        }

        var root = ProbeSettings.RepositoryRoot() ?? throw new ProbeException("GeoClubBot.sln was not found above the probe.");
        var settings = BotSettings.Load(BotSettings.DefaultPath(root), arguments.Overrides ?? [], apiKey);
        var botCollection = ProbeSettings.BotCollectionName(settings.Ai);
        if (arguments.Collection is null && botCollection != context.Settings.Collection)
        {
            throw new ProbeException(
                $"The bot's settings name the collection '{botCollection}', not '{context.Settings.Collection}'. Pass --collection to choose.");
        }

        var questions = Select(context);
        Console.Error.WriteLine($"Answering {questions.Count} rated answer(s) with the OpenRouter key from {context.Settings.ApiKeySource}; "
                                + $"settings {settings.Fingerprint}{(settings.Overrides.Count == 0 ? string.Empty : $" ({string.Join(", ", settings.Overrides)})")}.");

        using var stack = AnsweringStack.Create(context, settings);
        var cache = new AnswerCache(Path.Combine(AppContext.BaseDirectory, CacheDirectoryName));

        var setup = new RequestAttribution();
        RequestAttribution.Current = setup;
        var refreshed = await stack.Catalog.RefreshAsync(context.CancellationToken).ConfigureAwait(false);
        RequestAttribution.Current = null;
        if (refreshed.IsFailure)
        {
            throw new ProbeException($"The model roster could not be read: {refreshed.Error.Message}");
        }

        var textChain = await stack.Catalog.ReadChainAsync(
            new ChatModelRequirements(MinContextLength: settings.Ai.OpenRouter.MinContextLength), context.CancellationToken).ConfigureAwait(false);
        var toolChain = settings.Tools.Enabled
            ? await stack.Catalog.ReadChainAsync(
                new ChatModelRequirements(NeedsTools: true, MinContextLength: settings.Ai.OpenRouter.MinContextLength), context.CancellationToken).ConfigureAwait(false)
            : [];
        Console.Error.WriteLine($"Roster: {refreshed.Value} free models. Text chain: {string.Join(", ", textChain)}; tool chain: {string.Join(", ", toolChain)}.");

        var runs = new AnswerRun?[questions.Count];
        await Parallel.ForEachAsync(
                Enumerable.Range(0, questions.Count),
                new ParallelOptions { MaxDegreeOfParallelism = arguments.Parallelism, CancellationToken = context.CancellationToken },
                async (index, cancellationToken) =>
                {
                    runs[index] = await AnswerAsync(stack, settings, cache, questions[index], cancellationToken).ConfigureAwait(false);
                })
            .ConfigureAwait(false);

        IReadOnlyList<string>? judgeChain = null;
        var verdicts = new JudgeVerdict?[questions.Count];
        var judging = new RequestAttribution();
        if (arguments.Judge)
        {
            judgeChain = arguments.JudgeModels is { Count: > 0 } named
                ? named
                : await DefaultJudgeChainAsync(stack.Catalog, settings, context.CancellationToken).ConfigureAwait(false);
            Console.Error.WriteLine($"Judging with {string.Join(", ", judgeChain)}.");

            await Parallel.ForEachAsync(
                    Enumerable.Range(0, questions.Count),
                    new ParallelOptions { MaxDegreeOfParallelism = arguments.Parallelism, CancellationToken = context.CancellationToken },
                    async (index, cancellationToken) =>
                    {
                        if (runs[index] is not { Answer: { } answer } run)
                        {
                            return;
                        }

                        RequestAttribution.Current = judging;
                        var (message, pictures) = AnswerReport.Posted(answer, settings.ImageRelay);
                        var messages = AnswerJudge.BuildMessages(questions[index], run, message, pictures);
                        verdicts[index] = await AnswerJudge
                            .JudgeAsync(stack.Judge, judgeChain, messages, cache, run.Key, cancellationToken)
                            .ConfigureAwait(false);
                    })
                .ConfigureAwait(false);
        }

        var header = new AnswerReportHeader(
            Path.GetFileName(arguments.Target!),
            context.Settings.Collection,
            DateTimeOffset.UtcNow,
            settings,
            textChain,
            toolChain,
            judgeChain,
            setup.Embeddings + setup.Chats + setup.Roster,
            judging.Chats);

        var report = AnswerReport.Render(
            header,
            [.. questions.Select((question, index) => new AnswerReportRow(question, runs[index], verdicts[index]))]);
        foreach (var line in report.Split('\n'))
        {
            context.Report.Line(line.TrimEnd('\r'));
        }

        var asked = runs.OfType<AnswerRun>().ToList();
        Console.Error.WriteLine(
            $"OpenRouter this run: {stack.RequestsSent} request(s) — the roster {setup.Roster}, the judge {judging.Chats}. "
            + $"{asked.Count(run => run.Succeeded)} of {questions.Count} answered; failures are not cached, so a rerun asks them again. "
            + $"Answers are cached in {cache.Directory}.");
    }

    /// <summary>The rated answers to ask: --rating, then --only, then the first --limit of them.</summary>
    private static List<RatedQuestion> Select(ProbeContext context)
    {
        var questions = context.ReadRatedQuestions()
            .Where(question => context.Arguments.Only?.Matches(question) ?? true)
            .ToList();

        if (context.Arguments.Limit is { } limit)
        {
            questions = [.. questions.Take(limit)];
        }

        return questions.Count > 0 ? questions : throw new ProbeException("No rated answer in the export matched --only.");
    }

    /// <summary>The cached answer, or the question asked again now. A failure is reported, not cached.</summary>
    private static async Task<AnswerRun> AnswerAsync(
        AnsweringStack stack,
        BotSettings settings,
        AnswerCache cache,
        RatedQuestion rated,
        CancellationToken cancellationToken)
    {
        var key = AnswerCache.KeyFor(rated.Record.FeedbackId, settings.Fingerprint);
        if (cache.ReadAnswer(key) is { } cached)
        {
            Console.Error.WriteLine($"#{rated.Number}: from the cache — {HitDescriptions.Cell(rated.Question, 60)}");
            return cached;
        }

        var attribution = new RequestAttribution();
        var trace = new AnswerTrace();
        RequestAttribution.Current = attribution;
        AnswerTrace.Current = trace;

        var branch = AnswerBranch.Create(rated, DateTimeOffset.UtcNow);
        var budget = new InMemoryBudgetRepository();
        var handler = stack.CreateHandler(new InMemoryConversationRepository(branch.Turns), budget);

        var started = Stopwatch.GetTimestamp();
        var result = await handler.Handle(branch.Command, cancellationToken).ConfigureAwait(false);
        var seconds = Stopwatch.GetElapsedTime(started).TotalSeconds;

        var run = new AnswerRun(
            key,
            rated.Record.FeedbackId,
            rated.Number,
            DateTimeOffset.UtcNow,
            result.IsSuccess ? result.Value : null,
            result.IsFailure ? result.Error.Code : null,
            result.IsFailure ? result.Error.Message : null,
            trace.Calls,
            new AnswerCost(attribution.Embeddings, attribution.Chats, budget.Claimed, budget.PromptTokens, budget.CompletionTokens),
            seconds,
            AnswerRun.DescribeExcerpts(trace.LastMessages),
            branch.Notes,
            trace.Log,
            trace.LastAnswerText);

        Console.Error.WriteLine(
            $"#{rated.Number}: {(run.Succeeded ? $"answered by {run.Answer!.ModelUsed}" : $"failed ({run.ErrorCode})")} in {seconds:0.0} s, "
            + $"{run.Answer?.ToolCalls?.Count ?? 0} tool call(s), {run.Cost.Requests} request(s) — {HitDescriptions.Cell(rated.Question, 60)}");

        if (run.Succeeded)
        {
            cache.WriteAnswer(run);
        }

        return run;
    }

    /// <summary>The best free text model of today's chain, the fallback router aside: it picks at random.</summary>
    private static async Task<IReadOnlyList<string>> DefaultJudgeChainAsync(
        IChatModelCatalog catalog,
        BotSettings settings,
        CancellationToken cancellationToken)
    {
        var chain = await catalog.ReadChainAsync(
            new ChatModelRequirements(MinContextLength: Math.Max(settings.Ai.OpenRouter.MinContextLength, AnswerJudge.MinContextLength)),
            cancellationToken).ConfigureAwait(false);

        var best = chain.FirstOrDefault(model =>
            !string.Equals(model, settings.Ai.OpenRouter.FallbackModelId, StringComparison.OrdinalIgnoreCase));

        return best is null
            ? throw new ProbeException("No free text model qualifies to judge today; name one with --judge-model.")
            : [best];
    }
}

/// <summary>
/// The bot's answering services, built as its host builds them, over clients the probe guards: OpenRouter
/// behind <see cref="EmbeddingsOnlyHandler"/> (embeddings, chat, the roster and model health only) and
/// <see cref="PacedRetryHandler"/>, GBIF behind a handler that only reads.
/// </summary>
public sealed class AnsweringStack : IDisposable
{
    private readonly ILoggerFactory _logging;
    private readonly HttpClient _openRouter;
    private readonly HttpClient _gbif;
    private readonly EmbeddingsOnlyHandler _guard;
    private readonly IOptions<AiConfiguration> _ai;
    private readonly IOptions<AiConversationConfiguration> _conversation;
    private readonly IOptions<AiRetrievalConfiguration> _retrieval;
    private readonly IOptions<AiToolsConfiguration> _tools;
    private readonly IEmbedder _embedder;
    private readonly IKnowledgeIndex _index;
    private readonly IChatModelClient _chat;
    private readonly AnswerToolbox _toolbox;
    private readonly PlantRangeLookAhead _plantRange;
    private readonly KnowledgeCountryDirectory _countries;
    private readonly QueryPlannerPause _plannerPause;

    private AnsweringStack(ProbeContext context, BotSettings settings)
    {
        _logging = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Debug).AddProvider(new AnswerTraceLoggerProvider()));
        _ai = Options.Create(settings.Ai);
        _conversation = Options.Create(settings.Conversation);
        _retrieval = Options.Create(settings.Retrieval);
        _tools = Options.Create(settings.Tools);

        var openRouter = settings.Ai.OpenRouter;
        _guard = new EmbeddingsOnlyHandler(new HttpClientHandler(), allowChat: true);
        _openRouter = new HttpClient(new PacedRetryHandler(
            _guard, openRouter.PerMinuteRequestBudget, TimeSpan.FromSeconds(settings.Ai.RequestTimeoutSeconds)))
        {
            BaseAddress = new Uri(openRouter.BaseUrl),
            Timeout = TimeSpan.FromSeconds(Math.Max(settings.Ai.OverallTimeoutSeconds, settings.Ai.RequestTimeoutSeconds))
        };
        _openRouter.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", openRouter.ApiKey);
        _openRouter.DefaultRequestHeaders.Add("X-Title", $"{openRouter.AppName} retrieval probe");

        var species = settings.Tools.SpeciesRange;
        _gbif = new HttpClient(new ReadOnlyHttpHandler(new HttpClientHandler()))
        {
            Timeout = TimeSpan.FromSeconds(species.RequestTimeoutSeconds * 2 + 1)
        };
        _gbif.DefaultRequestHeaders.UserAgent.ParseAdd("GeoClubBot/1.0 (+https://github.com/efibs/geo-club-bot)");

        var clients = new NamedClientFactory(new Dictionary<string, HttpClient>(StringComparer.Ordinal)
        {
            [RefitChatModelClient.HttpClientName] = _openRouter,
            [RefitChatModelClient.HealthHttpClientName] = _openRouter,
            [GbifSpeciesRangeReader.HttpClientName] = _gbif
        });

        var clock = TimeProvider.System;
        Judge = new RefitChatModelClient(clients, _logging.CreateLogger<RefitChatModelClient>());
        _chat = new RecordingChatModelClient(Judge);
        _embedder = new OpenRouterEmbedder(clients, _ai, _logging.CreateLogger<OpenRouterEmbedder>());
        _index = context.Index;
        Catalog = new ChatModelCatalog(_chat, _ai, clock, _logging.CreateLogger<ChatModelCatalog>());
        _countries = new KnowledgeCountryDirectory(_index, _retrieval, clock, _logging.CreateLogger<KnowledgeCountryDirectory>());
        _plannerPause = new QueryPlannerPause(clock);
        // One reader for the tool and the lookup before answering, as the bot's singleton is shared.
        var speciesReader = new GbifSpeciesRangeReader(clients, _tools, clock, _logging.CreateLogger<GbifSpeciesRangeReader>());
        _toolbox = new AnswerToolbox(
            _embedder, _index, speciesReader, _countries, _retrieval, _tools, _logging.CreateLogger<AnswerToolbox>());
        _plantRange = new PlantRangeLookAhead(speciesReader, _tools, clock, _logging.CreateLogger<PlantRangeLookAhead>());
    }

    /// <summary>Shared by every answer, as the bot's singleton is: a model that fails for one is demoted for the next.</summary>
    public IChatModelCatalog Catalog { get; }

    /// <summary>The chat client without the recording: the judge's requests are not the answer's.</summary>
    public IChatModelClient Judge { get; }

    /// <summary>Every request that left for OpenRouter, retries included.</summary>
    public int RequestsSent => _guard.RequestsSent;

    public static AnsweringStack Create(ProbeContext context, BotSettings settings) => new(context, settings);

    /// <summary>
    /// The handler for one answer, over its own conversation store and allowance — the scoped services,
    /// as the bot's host would create them per message.
    /// </summary>
    public AskAiHandler CreateHandler(InMemoryConversationRepository conversations, InMemoryBudgetRepository budget)
    {
        var clock = TimeProvider.System;
        var loop = new AiAnswerLoop(_chat, Catalog, budget, _toolbox, _ai, _tools, clock, _logging.CreateLogger<AiAnswerLoop>());

        return new AskAiHandler(
            conversations, budget, _embedder, _index, Catalog, _chat, loop, _plantRange, _countries, _plannerPause,
            _ai, _conversation, _retrieval, clock, _logging.CreateLogger<AskAiHandler>());
    }

    public void Dispose()
    {
        _openRouter.Dispose();
        _gbif.Dispose();
        _logging.Dispose();
    }

    private sealed class NamedClientFactory(IReadOnlyDictionary<string, HttpClient> clients) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            clients.TryGetValue(name, out var client)
                ? client
                : throw new InvalidOperationException($"GeoClubBot.RetrievalProbe has no '{name}' client: refusing to make one.");
    }
}

/// <summary>Lets only reads through: the species-range tool's GBIF lookups are GETs, and nothing else may leave.</summary>
public sealed class ReadOnlyHttpHandler(HttpMessageHandler innerHandler) : DelegatingHandler(innerHandler)
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        request.Method == HttpMethod.Get
            ? base.SendAsync(request, cancellationToken)
            : throw new InvalidOperationException($"GeoClubBot.RetrievalProbe only reads GBIF: refusing to send {request.Method} {request.RequestUri}.");
}
