using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Configuration;
using FluentAssertions;
using Infrastructure.OutputAdapters.AI.OpenRouter;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using UseCases.OutputPorts.AI;
using Xunit;
using Xunit.Abstractions;

namespace GeoClubBot.Tests.AI;

/// <summary>
/// Replays the free roster OpenRouter served on 2026-10-23, and each model's provider health from that
/// day, through the whole ranking path: the Refit client parses the roster and the endpoints, the
/// catalog reads health on refresh, the selector ranks.
///
/// That day's chain was <c>[dots-3-note-preview, inkling-small, inkling]</c>: two of its three slots
/// went to models that answer this bot with 403, and the ranking had no notion of size or provider
/// health. Every rule of the reworked ranking is pinned here against the roster that showed the need
/// for it, rather than against synthetic models that could be tuned to pass.
/// </summary>
public sealed class FreeModelRosterReplayTests(ITestOutputHelper output)
{
    /// <summary>Noon UTC on the day the roster and the endpoints were captured.</summary>
    private static readonly DateTimeOffset CapturedAt = new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);

    private const string Ultra = "nvidia/nemotron-3-ultra-550b-a55b:free";
    private const string SuperVl = "nvidia/nemotron-3.5-super-vl:free";
    private const string Dots = "dots-studio/dots-3-note-preview:free";
    private const string Gemma31 = "google/gemma-4-31b-it:free";
    private const string Inkling = "thinkingmachines/inkling:free";
    private const string InklingSmall = "thinkingmachines/inkling-small:free";

    /// <summary>Too small (LFM, 2.6B), or behind a provider that served next to nothing all day.</summary>
    private static readonly string[] SmallOrDown =
    [
        "liquid/lfm-2.5-2.6b:free",
        "nvidia/nemotron-3-super-120b-a12b:free",
        "nvidia/nemotron-3-nano-omni-30b-a3b-reasoning:free"
    ];

    /// <summary>Described by their providers as built for coding agents, or named as coding models.</summary>
    private static readonly string[] CodingModels =
    [
        "recursive/flint-v1.0:free",
        "poolside/laguna-s-2.1:free",
        "poolside/laguna-xs-2.1:free",
        "cohere/north-mini-code:free"
    ];

    [Fact]
    public async Task TheChains_GoToTheModelsThatAnsweredBest_WithTheRecommendedPreference()
    {
        // Ultra gave the best grounded answer of the day; Super VL the fastest correct one, and the only
        // parallel tool calls. Preferred in that order, they take the head of the chains.
        var (catalog, _) = await RefreshAsync(preferred: ["nvidia/nemotron-3-ultra", "nvidia/nemotron-3.5-super-vl"]);

        // What a 403 "only available on agentic harnesses" does to each of them.
        catalog.ReportUnfit(Inkling);
        catalog.ReportUnfit(InklingSmall);

        var text = await catalog.ReadChainAsync(new ChatModelRequirements());
        var image = await catalog.ReadChainAsync(new ChatModelRequirements(NeedsImageInput: true));

        await PrintAsync(catalog);

        text.Should().Equal(Ultra, SuperVl, Dots);
        image.Should().Equal(SuperVl, Dots, Gemma31);
    }

    [Fact]
    public async Task TheShippedSettings_GiveTheRecommendedChains_WithoutWaitingForARefusal()
    {
        // appsettings.json as it ships: its preferences and its blocked Inkling models, so production
        // starts on these chains rather than learning the 403s one question at a time.
        var openRouter = LoadShippedOpenRouterSettings();
        var (catalog, _) = await RefreshAsync(openRouter);

        var text = await catalog.ReadChainAsync(new ChatModelRequirements(MinContextLength: openRouter.MinContextLength));
        var image = await catalog.ReadChainAsync(
            new ChatModelRequirements(NeedsImageInput: true, MinContextLength: openRouter.MinContextLength));

        await PrintAsync(catalog);

        text.Should().Equal(Ultra, SuperVl, Dots);
        image.Should().Equal(SuperVl, Dots, Gemma31);
    }

    [Fact]
    public async Task WithoutAPreference_TheChainsAreRankedOnSizeAndHealthAlone()
    {
        var (catalog, _) = await RefreshAsync(preferred: []);
        catalog.ReportUnfit(Inkling);
        catalog.ReportUnfit(InklingSmall);

        var text = await catalog.ReadChainAsync(new ChatModelRequirements());
        var image = await catalog.ReadChainAsync(new ChatModelRequirements(NeedsImageInput: true));

        await PrintAsync(catalog);

        // Super VL, a week old, is still ramping in; Gemma 31B, dense and settled, edges past it.
        text.Should().Equal(Ultra, Dots, Gemma31);
        image.Should().Equal(Dots, Gemma31, SuperVl);
    }

    [Fact]
    public async Task WithoutAPreference_SuperVlOvertakesGemma_OnceItHasSettledIn()
    {
        // The same roster a week on, with no operator action: the ranking moves by itself.
        var (catalog, _) = await RefreshAsync(preferred: [], now: CapturedAt.AddDays(7));
        catalog.ReportUnfit(Inkling);
        catalog.ReportUnfit(InklingSmall);

        (await catalog.ReadChainAsync(new ChatModelRequirements())).Should().Equal(Ultra, Dots, SuperVl);
        (await catalog.ReadChainAsync(new ChatModelRequirements(NeedsImageInput: true)))
            .Should().Equal(Dots, SuperVl, Gemma31);
    }

    [Fact]
    public async Task TheRanking_LeavesOutTheSmallAndTheDown_AndPutsTheCodingModelsLast()
    {
        var (catalog, _) = await RefreshAsync(preferred: []);

        var ranking = await catalog.ReadRankingAsync(new ChatModelRequirements());
        var vision = await catalog.ReadRankingAsync(new ChatModelRequirements(NeedsImageInput: true));

        // Before either refuses, the gated models still rank — at the top, being the largest: nothing in
        // the roster or in their health says they will refuse. Only the 403 does.
        ranking.Select(candidate => candidate.Id).Should().StartWith([Inkling, Ultra]);

        var ids = ranking.Select(candidate => candidate.Id).ToList();
        ids.Should().NotContain(SmallOrDown)
            .And.NotContain(["nvidia/nemotron-3.5-content-safety:free", "google/lyria-3-pro-preview", "openrouter/free"]);

        // Demoted rather than excluded: when nothing else is left, a coding model still beats the router.
        ids.TakeLast(CodingModels.Length).Should().BeEquivalentTo(CodingModels);

        vision.Select(candidate => candidate.Id).Should()
            .NotContain("nvidia/nemotron-3-nano-omni-30b-a3b-reasoning:free", "13% uptime over the day");
    }

    [Fact]
    public async Task Refresh_ReadsTheHealthOfTheModelsAChainCouldName_AndNoMore()
    {
        var (catalog, handler) = await RefreshAsync(preferred: [], healthCheckModelCount: 10);

        handler.EndpointReads.Should().HaveCount(10);
        handler.EndpointReads.Should().Contain([Ultra, SuperVl, Dots, Gemma31]);
        handler.EndpointReads.Should().NotContain("liquid/lfm-2.5-2.6b:free", "it is too small to be asked whatever its health");

        (await catalog.ReadRankingAsync(new ChatModelRequirements()))
            .Single(candidate => candidate.Id == SuperVl).Model.UptimeLastDayPercent
            .Should().BeApproximately(91.1, 0.1);
    }

    [Fact]
    public async Task TheRosterParsesEveryStatedSize()
    {
        var (catalog, _) = await RefreshAsync(preferred: []);

        var sizes = (await catalog.ReadRankingAsync(new ChatModelRequirements(MinContextLength: 0)))
            .ToDictionary(candidate => candidate.Id, candidate => candidate.Model.EffectiveParametersBillions);

        sizes[Ultra].Should().BeApproximately(173.9, 0.1, "550B total, 55B active, from the id");
        sizes[Dots].Should().BeApproximately(66.9, 0.1, "\"16B active parameters out of 280B total\"");
        sizes[SuperVl].Should().BeApproximately(37.9, 0.1, "\"12B active parameters out of 120B total\"");
        sizes[Gemma31].Should().Be(31, "dense, from the id");
    }

    private static Task<(ChatModelCatalog Catalog, RosterHandler Handler)> RefreshAsync(
        List<string> preferred,
        int healthCheckModelCount = 10,
        DateTimeOffset? now = null) =>
        RefreshAsync(
            new OpenRouterConfiguration
            {
                FallbackModelId = "openrouter/free",
                ChainLength = 3,
                PreferredModelPrefixes = preferred,
                HealthCheckModelCount = healthCheckModelCount
            },
            now);

    private static async Task<(ChatModelCatalog Catalog, RosterHandler Handler)> RefreshAsync(
        OpenRouterConfiguration openRouter,
        DateTimeOffset? now = null)
    {
        var handler = new RosterHandler(
            await ReadFixtureAsync("openrouter-models-20261023.json"),
            JsonDocument.Parse(await ReadFixtureAsync("openrouter-endpoints-20261023.json")));

        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://openrouter.ai") };
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(RefitChatModelClient.HttpClientName).Returns(httpClient);
        factory.CreateClient(RefitChatModelClient.HealthHttpClientName).Returns(httpClient);

        var client = new RefitChatModelClient(factory, NullLogger<RefitChatModelClient>.Instance);
        var configuration = Options.Create(new AiConfiguration { OpenRouter = openRouter });

        var catalog = new ChatModelCatalog(
            client, configuration, new FixedTimeProvider(now ?? CapturedAt), NullLogger<ChatModelCatalog>.Instance);

        var refreshed = await catalog.RefreshAsync();
        refreshed.IsSuccess.Should().BeTrue();

        return (catalog, handler);
    }

    private async Task PrintAsync(ChatModelCatalog catalog)
    {
        foreach (var (label, requirements) in new[]
                 {
                     ("text", new ChatModelRequirements()),
                     ("image", new ChatModelRequirements(NeedsImageInput: true))
                 })
        {
            output.WriteLine($"-- {label} ranking --");
            foreach (var candidate in await catalog.ReadRankingAsync(requirements))
            {
                output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"{candidate.Score,7:0.000}  {candidate.Id}  eff={candidate.Model.EffectiveParametersBillions:0.#}B up={candidate.Model.UptimeLastDayPercent:0.#} demotion={candidate.Demotion:0.##}"));
            }
        }
    }

    /// <summary>The <c>AI:OpenRouter</c> section of the API's appsettings.json, found by walking up to the solution root.</summary>
    private static OpenRouterConfiguration LoadShippedOpenRouterSettings()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GeoClubBot.sln")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull("the solution root should be discoverable from the test run");

        var settings = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(directory!.FullName, "GeoClubBot.API", "appsettings.json"))
            .Build();

        return settings.GetSection("AI:OpenRouter").Get<OpenRouterConfiguration>()
               ?? throw new InvalidOperationException("appsettings.json has no AI:OpenRouter section.");
    }

    private static Task<string> ReadFixtureAsync(string name) =>
        File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "AI", name));

    /// <summary>
    /// Serves the captured roster on <c>/api/v1/models</c> and each model's captured endpoints on its
    /// own route; a model nobody captured answers 404, as an unknown model does.
    /// </summary>
    private sealed class RosterHandler(string roster, JsonDocument endpoints) : HttpMessageHandler
    {
        private const string ModelsPath = "/api/v1/models";

        public List<string> EndpointReads { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = Uri.UnescapeDataString(request.RequestUri!.AbsolutePath);

            if (path == ModelsPath)
            {
                return Task.FromResult(Json(HttpStatusCode.OK, roster, request));
            }

            if (path.StartsWith(ModelsPath + "/", StringComparison.Ordinal) && path.EndsWith("/endpoints", StringComparison.Ordinal))
            {
                var modelId = path[(ModelsPath.Length + 1)..^"/endpoints".Length];
                EndpointReads.Add(modelId);

                return Task.FromResult(endpoints.RootElement.TryGetProperty(modelId, out var captured)
                    ? Json(HttpStatusCode.OK, captured.GetRawText(), request)
                    : Json(HttpStatusCode.NotFound, """{"error":{"message":"Not Found","code":404}}""", request));
            }

            return Task.FromResult(Json(HttpStatusCode.NotFound, "{}", request));
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string body, HttpRequestMessage request) =>
            new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json"), RequestMessage = request };
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
