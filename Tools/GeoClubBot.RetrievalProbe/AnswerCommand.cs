using System.Diagnostics;
using System.Net.Http.Headers;
using Configuration;
using Entities;
using Infrastructure.OutputAdapters.AI.Gbif;
using Infrastructure.OutputAdapters.AI.GeoNn;
using Infrastructure.OutputAdapters.AI.OpenRouter;
using Infrastructure.OutputAdapters.AI.PlantNet;
using Infrastructure.OutputAdapters.AI.Regions;
using Infrastructure.OutputAdapters.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.OutputPorts.AI;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.OutputPorts.Repositories;
using UseCases.UseCases.AI.Conversations;
using UseCases.UseCases.AI.Retrieval;
using UseCases.UseCases.AI.Review;
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
/// behind <see cref="EmbeddingsOnlyHandler"/> (embeddings, chat, the roster and model health only),
/// <see cref="PacedRetryHandler"/> and the bot's <see cref="ResponseBodyDeadlineHandler"/>, GBIF behind a
/// handler that only reads.
/// </summary>
public sealed class AnsweringStack : IDisposable
{
    private readonly ILoggerFactory _logging;
    private readonly HttpClient _openRouter;
    private readonly HttpClient _gbif;
    private readonly HttpClient _naturalEarth;
    private readonly HttpClient _geoNn;
    private readonly IHttpClientFactory _clients;
    private readonly EmbeddingsOnlyHandler _guard;
    private readonly IOptions<AiConfiguration> _ai;
    private readonly IOptions<AiConversationConfiguration> _conversation;
    private readonly IOptions<AiRetrievalConfiguration> _retrieval;
    private readonly IOptions<AiCaptionsConfiguration> _captions;
    private readonly IOptions<AiToolsConfiguration> _tools;
    private readonly IEmbedder _embedder;
    private readonly IKnowledgeIndex _index;
    private readonly IChatModelClient _chat;
    private readonly AnswerToolbox _toolbox;
    private readonly PlantRangeLookAhead _plantRange;
    private readonly PlantIdentificationLookAhead _plantIdentification;
    private readonly RegionLocator _regionLocator;
    private readonly MainGuideOverlap _mainGuide;
    private readonly ClueSampler _clueSampler;
    private readonly KnowledgeCountryDirectory _countries;
    private readonly QueryPlannerPause _plannerPause;

    private AnsweringStack(ProbeContext context, BotSettings settings)
    {
        _logging = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Debug).AddProvider(new AnswerTraceLoggerProvider()));
        _ai = Options.Create(settings.Ai);
        _conversation = Options.Create(settings.Conversation);
        _retrieval = Options.Create(settings.Retrieval);
        _captions = Options.Create(settings.Captions);
        _tools = Options.Create(settings.Tools);

        var openRouter = settings.Ai.OpenRouter;
        var attemptTimeout = TimeSpan.FromSeconds(settings.Ai.RequestTimeoutSeconds);
        _guard = new EmbeddingsOnlyHandler(new HttpClientHandler(), allowChat: true);

        // The bot's own guard against a response that opens and never ends, outside the pacing and the
        // retries as the bot has it outside its pipeline: without it one stalled model held a replay
        // for 57 minutes (2026-11-11), and the bot's answer to it could not be seen.
        _openRouter = new HttpClient(new ResponseBodyDeadlineHandler(TimeSpan.FromSeconds(Math.Max(1, settings.Ai.ResponseTimeoutSeconds)))
        {
            InnerHandler = new PacedRetryHandler(_guard, openRouter.PerMinuteRequestBudget, attemptTimeout)
        })
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

        // The region outlines, downloaded once next to the probe, as the bot keeps them in its data directory.
        _naturalEarth = new HttpClient(new ReadOnlyHttpHandler(new HttpClientHandler())) { Timeout = TimeSpan.FromMinutes(5) };
        _naturalEarth.DefaultRequestHeaders.UserAgent.ParseAdd("GeoClubBot/1.0 (+https://github.com/efibs/geo-club-bot)");

        // GeoNN's prediction is a POST that changes nothing; it is the one request its client may send.
        _geoNn = new HttpClient(new GeoNnPredictOnlyHandler(new HttpClientHandler())) { Timeout = Timeout.InfiniteTimeSpan };

        var clients = new NamedClientFactory(new Dictionary<string, HttpClient>(StringComparer.Ordinal)
        {
            [GeoNnStreetViewGuesser.HttpClientName] = _geoNn,
            [RefitChatModelClient.HttpClientName] = _openRouter,
            [RefitChatModelClient.HealthHttpClientName] = _openRouter,
            [GbifSpeciesRangeReader.HttpClientName] = _gbif,
            [NaturalEarthRegionBoundaries.HttpClientName] = _naturalEarth
        });

        _clients = clients;
        var clock = TimeProvider.System;
        // Kept next to the probe rather than wherever it is run from; read by nothing else.
        settings.Tools.RegionMap.DataDirectory = Path.Combine(AppContext.BaseDirectory, "ai-data");
        Boundaries = new NaturalEarthRegionBoundaries(clients, _tools, _logging.CreateLogger<NaturalEarthRegionBoundaries>());
        Judge = new RefitChatModelClient(clients, _logging.CreateLogger<RefitChatModelClient>());
        _chat = new RecordingChatModelClient(Judge);
        _embedder = new OpenRouterEmbedder(clients, _ai, _logging.CreateLogger<OpenRouterEmbedder>());
        _index = context.Index;
        Catalog = new ChatModelCatalog(_chat, _ai, clock, _logging.CreateLogger<ChatModelCatalog>());
        _countries = new KnowledgeCountryDirectory(_index, _retrieval, clock, _logging.CreateLogger<KnowledgeCountryDirectory>());
        _plannerPause = new QueryPlannerPause(clock);
        // One reader for the tool and the lookup before answering, as the bot's singleton is shared.
        var speciesReader = new GbifSpeciesRangeReader(clients, _tools, clock, _logging.CreateLogger<GbifSpeciesRangeReader>());

        // The bot's own map renderer, reading GBIF's tiles through the same GET-only client; what it
        // draws is kept in a folder instead of the bot's image relay.
        var speciesMap = new GbifSpeciesRangeMap(
            clients, new SpeciesMapFolder(Path.Combine(AppContext.BaseDirectory, "species-maps")), _tools, clock,
            _logging.CreateLogger<GbifSpeciesRangeMap>());

        _mainGuide = new MainGuideOverlap(_index, _countries, _retrieval, _logging.CreateLogger<MainGuideOverlap>());
        _clueSampler = new ClueSampler(_index, _mainGuide, _retrieval, _logging.CreateLogger<ClueSampler>());
        _toolbox = new AnswerToolbox(
            _embedder, _index, speciesReader, speciesMap, new NoRegionBoundaries(), new NoRegionMapDrawer(), _mainGuide, _countries,
            _retrieval, _tools, _logging.CreateLogger<AnswerToolbox>());
        _plantRange = new PlantRangeLookAhead(speciesReader, speciesMap, _tools, clock, _logging.CreateLogger<PlantRangeLookAhead>());
        // Off unless the bot's settings carry a Pl@ntNet key; a rated screenshot's link has usually expired anyway.
        var plantIdentifier = new PlantNetPlantIdentifier(clients, _tools, clock, _logging.CreateLogger<PlantNetPlantIdentifier>());
        _plantIdentification = new PlantIdentificationLookAhead(
            plantIdentifier, speciesReader, speciesMap, _tools, clock, _logging.CreateLogger<PlantIdentificationLookAhead>());
        // The probe reports answers as text; a region map would only be a link to a picture it cannot show.
        _regionLocator = new RegionLocator(new NoRegionBoundaries(), new NoRegionMapDrawer(), _countries, _tools, clock, _logging.CreateLogger<RegionLocator>());
    }

    /// <summary>Shared by every answer, as the bot's singleton is: a model that fails for one is demoted for the next.</summary>
    public IChatModelCatalog Catalog { get; }

    /// <summary>The chat client without the recording: the judge's requests are not the answer's.</summary>
    public IChatModelClient Judge { get; }

    /// <summary>Natural Earth's region outlines, as the bot reads them (<c>review</c> places rounds and guesses with them).</summary>
    public IRegionBoundaries Boundaries { get; }

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
            conversations, budget, _embedder, _index, Catalog, _chat, loop, _plantRange, _plantIdentification, _regionLocator, _mainGuide, _clueSampler, _countries,
            _plannerPause,
            _ai, _conversation, _retrieval, _captions,
            Options.Create(new AiKnowledgeGapsConfiguration { RecordWeakRetrievals = false }), new NoRetrievalGaps(),
            clock, _logging.CreateLogger<AskAiHandler>());
    }

    /// <summary>
    /// The round review over the given player and challenge results, the probe's index, models and
    /// region outlines: the bot's handler, with what it would read from the database and GeoGuessr handed in.
    /// </summary>
    public ReviewRoundsHandler CreateReviewHandler(
        IGeoGuessrUserRepository users,
        IGeoGuessrClientFactory geoGuessr,
        InMemoryBudgetRepository budget,
        AiRoundReviewConfiguration settings)
    {
        var clock = TimeProvider.System;
        var loop = new AiAnswerLoop(_chat, Catalog, budget, _toolbox, _ai, _tools, clock, _logging.CreateLogger<AiAnswerLoop>());
        var mainClub = Options.Create(new GeoGuessrConfiguration
        {
            SyncSchedule = "0 0 * * * ?",
            ActivityNcfaToken = "unused",
            UserProfileNcfaToken = "unused",
            Clubs = [new GeoGuessrClubEntry { ClubId = Guid.Empty, IsMain = true, NcfaToken = "unused" }]
        });

        var options = Options.Create(settings);
        var guesser = new GeoNnStreetViewGuesser(_clients, options, _logging.CreateLogger<GeoNnStreetViewGuesser>());
        return new ReviewRoundsHandler(
            users, ProbeFakes.Refusing<IClubChallengeRepository>(), geoGuessr, Boundaries, guesser, _embedder, _index, _countries, loop, budget,
            mainClub, _ai, options, clock, _logging.CreateLogger<ReviewRoundsHandler>());
    }

    public void Dispose()
    {
        _openRouter.Dispose();
        _gbif.Dispose();
        _naturalEarth.Dispose();
        _geoNn.Dispose();
        _logging.Dispose();
    }

    /// <summary>The probe records nothing: it replays questions, it does not ask them.</summary>
    private sealed class NoRetrievalGaps : IAiRetrievalGapRepository
    {
        public void Add(AiRetrievalGap gap)
        {
        }

        public Task<IReadOnlyList<AiRetrievalGap>> ReadSinceAsync(DateTimeOffset sinceUtc, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AiRetrievalGap>>([]);

        public Task<int> DeleteOlderThanAsync(DateTimeOffset cutoffUtc, CancellationToken cancellationToken = default) => Task.FromResult(0);
    }

    private sealed class NoRegionBoundaries : IRegionBoundaries
    {
        public Task<IReadOnlyList<RegionName>> ReadNamesAsync(string countryCode, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RegionName>>([]);

        public Task<RegionPlace?> LocateAsync(double latitude, double longitude, CancellationToken cancellationToken = default) =>
            Task.FromResult<RegionPlace?>(null);
    }

    private sealed class NoRegionMapDrawer : IRegionMapDrawer
    {
        public Task<string?> DrawAsync(RegionMapRequest request, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
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

/// <summary>Lets GeoNN's prediction through and nothing else: <c>POST /api/predict</c>.</summary>
public sealed class GeoNnPredictOnlyHandler(HttpMessageHandler innerHandler) : DelegatingHandler(innerHandler)
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        request.Method == HttpMethod.Post && request.RequestUri?.AbsolutePath == "/api/predict"
            ? base.SendAsync(request, cancellationToken)
            : throw new InvalidOperationException($"GeoClubBot.RetrievalProbe only asks GeoNN for predictions: refusing {request.Method} {request.RequestUri}.");
}
