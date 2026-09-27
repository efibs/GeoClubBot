using Configuration;
using FluentAssertions;
using Infrastructure.OutputAdapters.AI.OpenRouter;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using UseCases.OutputPorts.AI;
using Utilities;
using Xunit;

namespace GeoClubBot.Tests.AI;

/// <summary>
/// Covers the catalog's stateful behaviour: degrading safely when the provider is unreachable, and
/// demoting models that just failed without blocking them forever.
/// </summary>
public sealed class ChatModelCatalogTests
{
    private static readonly DateTimeOffset Start = new(2026, 8, 20, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ReadChain_ReturnsTheFallbackRouterAlone_BeforeAnyRefreshHasSucceeded()
    {
        // The cold-start / degraded path. The bot must still answer rather than erroring out, because
        // the router picks a free model itself and filters for the features the request needs.
        var (catalog, _, _) = CreateCatalog();

        var chain = await catalog.ReadChainAsync(new ChatModelRequirements());

        chain.Should().Equal("openrouter/free");
        catalog.ReadStatus().Source.Should().Be(AiCatalogSource.None);
    }

    [Fact]
    public async Task Refresh_PopulatesTheRoster_AndReportsWhatItFound()
    {
        var (catalog, client, _) = CreateCatalog();
        client.ReadFreeModelsAsync(Arg.Any<CancellationToken>()).Returns(
            Result<IReadOnlyList<ChatModelDescriptor>>.Success([Model("a/text"), Model("b/vision", supportsImageInput: true)]));

        var result = await catalog.RefreshAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(2);

        var status = catalog.ReadStatus();
        status.ModelCount.Should().Be(2);
        status.VisionModelCount.Should().Be(1);
        status.Source.Should().Be(AiCatalogSource.Live);
        status.LastRefreshedAtUtc.Should().Be(Start);
    }

    [Fact]
    public async Task Refresh_KeepsTheExistingRoster_WhenTheProviderIsUnreachable()
    {
        // A stale roster still beats collapsing to the router for every request: completions may well
        // still work even when the models endpoint is failing.
        var (catalog, client, _) = CreateCatalog();
        client.ReadFreeModelsAsync(Arg.Any<CancellationToken>()).Returns(
            Result<IReadOnlyList<ChatModelDescriptor>>.Success([Model("a/text")]));
        await catalog.RefreshAsync();

        client.ReadFreeModelsAsync(Arg.Any<CancellationToken>()).Returns(
            Result<IReadOnlyList<ChatModelDescriptor>>.Failure(
                Error.Unexpected("ai.model_roster_unavailable", "boom")));

        var result = await catalog.RefreshAsync();

        result.IsFailure.Should().BeTrue();
        catalog.ReadStatus().ModelCount.Should().Be(1, "the previously known roster must survive a failed refresh");
        (await catalog.ReadChainAsync(new ChatModelRequirements())).Should().Equal("a/text", "openrouter/free");
    }

    [Fact]
    public async Task ReportFailure_DemotesTheModel_ThenLetsItRecoverAsThePenaltyDecays()
    {
        var (catalog, client, time) = CreateCatalog();
        client.ReadFreeModelsAsync(Arg.Any<CancellationToken>()).Returns(
            Result<IReadOnlyList<ChatModelDescriptor>>.Success(
                [Model("flaky/model", contextLength: 512_000), Model("steady/model", contextLength: 500_000)]));
        await catalog.RefreshAsync();

        (await catalog.ReadChainAsync(new ChatModelRequirements()))[0].Should().Be("flaky/model");

        catalog.ReportFailure("flaky/model");
        catalog.ReportFailure("flaky/model");

        (await catalog.ReadChainAsync(new ChatModelRequirements()))[0].Should().Be("steady/model",
            "a model that just failed twice should not stay at the head of the chain");

        // Demotion is temporary by design — a transient upstream blip must not blacklist the best model.
        time.Now = Start.AddHours(1);

        (await catalog.ReadChainAsync(new ChatModelRequirements()))[0].Should().Be("flaky/model");
    }

    [Fact]
    public async Task ReadRanking_ExposesScoresForDiagnostics()
    {
        var (catalog, client, _) = CreateCatalog();
        client.ReadFreeModelsAsync(Arg.Any<CancellationToken>()).Returns(
            Result<IReadOnlyList<ChatModelDescriptor>>.Success([Model("a/text"), Model("b/text")]));
        await catalog.RefreshAsync();

        var ranking = await catalog.ReadRankingAsync(new ChatModelRequirements());

        ranking.Should().HaveCount(2);
        ranking[0].Score.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ReadChain_NeverNamesMoreModelsThanOneRequestMayCarry()
    {
        // OpenRouter refuses more than three. Clamping here rather than trimming on the wire keeps the
        // chain a caller is handed — and later blames, or excludes on a retry — the one actually sent.
        var (catalog, client, _) = CreateCatalog(chainLength: 5);
        await RefreshWithAsync(catalog, client,
            [.. Enumerable.Range(0, 6).Select(index => Model($"model/{index}", contextLength: 16_000 + index))]);

        (await catalog.ReadChainAsync(new ChatModelRequirements())).Should().HaveCount(3);
    }

    [Fact]
    public async Task ReportUnfit_KeepsTheModelOutOfLaterChains_AndFlagsItUnfit()
    {
        // A classifier whose description gave nothing away, caught only by what it answered. A failure
        // penalty would have faded within the hour and let it answer "User Safety: safe" again.
        var (catalog, client, time) = CreateCatalog();
        await RefreshWithAsync(catalog, client, [Model("sneaky/classifier", contextLength: 512_000), Model("real/model")]);

        catalog.ReportUnfit("sneaky/classifier");
        time.Now = Start.AddDays(1);

        (await catalog.ReadChainAsync(new ChatModelRequirements())).Should().NotContain("sneaky/classifier");
        catalog.IsUnfitToAnswer("SNEAKY/classifier").Should().BeTrue();
        catalog.ReadStatus().LearnedUnfitModelIds.Should().Equal("sneaky/classifier");
    }

    [Fact]
    public void ReportUnfit_IgnoresTheFallbackRouter()
    {
        // The router is not a model; "excluding" it would only clutter /ai status.
        var (catalog, _, _) = CreateCatalog();

        catalog.ReportUnfit("openrouter/free");

        catalog.ReadStatus().LearnedUnfitModelIds.Should().BeEmpty();
    }

    [Fact]
    public async Task IsUnfitToAnswer_RecognisesWhatTheRosterSaysIsNotAChatModel()
    {
        // The router's picks bypass the ranking, so its answers are screened against the same rules.
        var (catalog, client, _) = CreateCatalog(blocked: ["blocked/model"]);
        await RefreshWithAsync(catalog, client,
        [
            Model("nvidia/nemotron-3.5-content-safety:free", isGuardrail: true),
            Model("google/lyria-3-pro-preview", producesTextOnly: false),
            Model("real/model")
        ]);

        catalog.IsUnfitToAnswer("nvidia/nemotron-3.5-content-safety:free").Should().BeTrue();
        catalog.IsUnfitToAnswer("google/lyria-3-pro-preview").Should().BeTrue();
        catalog.IsUnfitToAnswer("blocked/model").Should().BeTrue();
        catalog.IsUnfitToAnswer("real/model").Should().BeFalse();
        catalog.IsUnfitToAnswer("unknown/model").Should().BeFalse("a stale roster is no reason to discard an answer");
    }

    [Fact]
    public async Task ReportChainOutcome_DemotesOnlyTheModelsSkippedBeforeTheAnswer()
    {
        var (catalog, client, _) = CreateCatalog();
        await RefreshWithAsync(catalog, client,
            [Model("a/model", contextLength: 512_000), Model("b/model", contextLength: 256_000), Model("c/model", contextLength: 128_000)]);

        catalog.ReportChainOutcome(["a/model", "b/model", "c/model"], answeredBy: "b/model");

        var ranking = await catalog.ReadRankingAsync(new ChatModelRequirements());
        ranking.Single(candidate => candidate.Id == "a/model").FailurePenalty.Should().BeGreaterThan(0);
        ranking.Single(candidate => candidate.Id == "b/model").FailurePenalty.Should().Be(0, "it answered");
        ranking.Single(candidate => candidate.Id == "c/model").FailurePenalty.Should().Be(0, "it was never reached");
    }

    [Fact]
    public async Task ReportChainOutcome_BlamesNothing_ForAnIdItCannotPlace()
    {
        var (catalog, client, _) = CreateCatalog();
        await RefreshWithAsync(catalog, client, [Model("a/model"), Model("b/model")]);

        catalog.ReportChainOutcome(["a/model", "b/model", "openrouter/free"], answeredBy: "a/model-20260901");

        (await catalog.ReadRankingAsync(new ChatModelRequirements()))
            .Should().OnlyContain(candidate => candidate.FailurePenalty == 0);
    }

    private static async Task RefreshWithAsync(
        ChatModelCatalog catalog,
        IChatModelClient client,
        IReadOnlyList<ChatModelDescriptor> roster)
    {
        client.ReadFreeModelsAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<ChatModelDescriptor>>.Success(roster));
        await catalog.RefreshAsync();
    }

    private static (ChatModelCatalog Catalog, IChatModelClient Client, FixedTimeProvider Time) CreateCatalog(
        int chainLength = 3,
        List<string>? blocked = null)
    {
        var client = Substitute.For<IChatModelClient>();
        var time = new FixedTimeProvider(Start);

        var configuration = Options.Create(new AiConfiguration
        {
            OpenRouter = new OpenRouterConfiguration
            {
                FallbackModelId = "openrouter/free",
                ChainLength = chainLength,
                BlockedModelIds = blocked ?? []
            }
        });

        return (new ChatModelCatalog(client, configuration, time, NullLogger<ChatModelCatalog>.Instance), client, time);
    }

    private static ChatModelDescriptor Model(
        string id,
        int contextLength = 64_000,
        bool supportsImageInput = false,
        bool producesTextOnly = true,
        bool isGuardrail = false) =>
        new(
            id,
            id,
            contextLength,
            MaxCompletionTokens: 8_192,
            supportsImageInput,
            producesTextOnly,
            SupportsTools: false,
            SupportsStructuredOutputs: false,
            isGuardrail,
            Start.AddDays(-60),
            ExpiresAt: null);

    /// <summary>
    /// TimeProvider is abstract with a virtual GetUtcNow, so a controllable clock needs no extra
    /// test package.
    /// </summary>
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
