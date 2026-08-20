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

    private static (ChatModelCatalog Catalog, IChatModelClient Client, FixedTimeProvider Time) CreateCatalog()
    {
        var client = Substitute.For<IChatModelClient>();
        var time = new FixedTimeProvider(Start);

        var configuration = Options.Create(new AiConfiguration
        {
            OpenRouter = new OpenRouterConfiguration { FallbackModelId = "openrouter/free", ChainLength = 3 }
        });

        return (new ChatModelCatalog(client, configuration, time, NullLogger<ChatModelCatalog>.Instance), client, time);
    }

    private static ChatModelDescriptor Model(
        string id,
        int contextLength = 64_000,
        bool supportsImageInput = false) =>
        new(id, id, contextLength, 8_192, supportsImageInput, false, false, Start.AddDays(-60), null);

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
