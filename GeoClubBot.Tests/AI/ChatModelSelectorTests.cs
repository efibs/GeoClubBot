using FluentAssertions;
using UseCases.OutputPorts.AI;
using Xunit;

namespace GeoClubBot.Tests.AI;

/// <summary>
/// The selector is the piece that keeps the bot working as the provider's free roster churns, so the
/// rules it enforces are pinned here rather than left to manual observation. Shapes and field values
/// mirror a real OpenRouter roster (free models, expiry dates, mixed modalities).
/// </summary>
public sealed class ChatModelSelectorTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 20, 12, 0, 0, TimeSpan.Zero);

    private const string Fallback = "openrouter/free";

    [Fact]
    public void SelectChain_RanksBiggerContextFirst_AndAlwaysEndsWithTheFallbackRouter()
    {
        var chain = ChatModelSelector.SelectChain(
            [Model("small/model", contextLength: 16_000), Model("big/model", contextLength: 512_000)],
            new ChatModelRequirements(),
            Options(),
            failurePenalties: null,
            Now);

        chain.Should().ContainInOrder("big/model", "small/model", Fallback);
        chain[^1].Should().Be(Fallback, "the router is the last resort and must always be reachable");
    }

    [Fact]
    public void SelectChain_ExcludesModelsRetiringInsideTheExpiryHorizon()
    {
        // A model retiring tomorrow would be picked, then vanish mid-day — the exact failure mode
        // that made pinning a single model id unreliable.
        var expiringTomorrow = Model("dying/model", contextLength: 512_000, expiresAt: Now.AddHours(24));
        var stable = Model("stable/model", contextLength: 64_000);

        var chain = ChatModelSelector.SelectChain(
            [expiringTomorrow, stable],
            new ChatModelRequirements(),
            Options(),
            failurePenalties: null,
            Now);

        chain.Should().NotContain("dying/model");
        chain.Should().ContainInOrder("stable/model", Fallback);
    }

    [Fact]
    public void SelectChain_KeepsModelsRetiringComfortablyBeyondTheHorizon()
    {
        var chain = ChatModelSelector.SelectChain(
            [Model("later/model", contextLength: 256_000, expiresAt: Now.AddDays(30))],
            new ChatModelRequirements(),
            Options(),
            failurePenalties: null,
            Now);

        chain.Should().ContainInOrder("later/model", Fallback);
    }

    [Fact]
    public void SelectChain_ExcludesBlockedModels()
    {
        var options = Options() with
        {
            BlockedModelIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "bad/model" }
        };

        var chain = ChatModelSelector.SelectChain(
            [Model("bad/model", contextLength: 512_000), Model("good/model", contextLength: 32_000)],
            new ChatModelRequirements(),
            options,
            failurePenalties: null,
            Now);

        chain.Should().NotContain("bad/model");
        chain.Should().ContainInOrder("good/model", Fallback);
    }

    [Fact]
    public void SelectChain_OffersOnlyVisionModels_WhenTheRequestCarriesAnImage()
    {
        var textOnly = Model("text/only", contextLength: 512_000);
        var vision = Model("sees/images", contextLength: 32_000, supportsImageInput: true);

        var chain = ChatModelSelector.SelectChain(
            [textOnly, vision],
            new ChatModelRequirements(NeedsImageInput: true),
            Options(),
            failurePenalties: null,
            Now);

        // The text-only model wins on context but cannot read the attachment at all.
        chain.Should().ContainInOrder("sees/images", Fallback);
        chain.Should().NotContain("text/only");
    }

    [Fact]
    public void SelectChain_ExcludesModelsWithoutToolSupport_WhenToolsAreRequired()
    {
        var chain = ChatModelSelector.SelectChain(
            [Model("no/tools", contextLength: 512_000), Model("has/tools", contextLength: 32_000, supportsTools: true)],
            new ChatModelRequirements(NeedsTools: true),
            Options(),
            failurePenalties: null,
            Now);

        chain.Should().ContainInOrder("has/tools", Fallback);
        chain.Should().NotContain("no/tools");
    }

    [Fact]
    public void SelectChain_ExcludesModelsBelowTheMinimumContextLength()
    {
        var chain = ChatModelSelector.SelectChain(
            [Model("tiny/model", contextLength: 4_096)],
            new ChatModelRequirements(MinContextLength: 8_192),
            Options(),
            failurePenalties: null,
            Now);

        chain.Should().Equal(Fallback);
    }

    [Fact]
    public void SelectChain_FallsBackToTheRouterAlone_WhenNothingQualifies()
    {
        // The degraded path: an empty roster still yields a usable chain, because the router picks a
        // free model itself and filters for the features the request needs.
        var chain = ChatModelSelector.SelectChain(
            [],
            new ChatModelRequirements(),
            Options(),
            failurePenalties: null,
            Now);

        chain.Should().Equal(Fallback);
    }

    [Fact]
    public void SelectChain_NeverListsTheFallbackRouterTwice()
    {
        // The router appears in the provider's own roster, so ranking it would let it occupy a slot
        // it is already guaranteed and push out a real candidate.
        var chain = ChatModelSelector.SelectChain(
            [Model(Fallback, contextLength: 200_000), Model("real/model", contextLength: 64_000)],
            new ChatModelRequirements(),
            Options(),
            failurePenalties: null,
            Now);

        chain.Should().Equal("real/model", Fallback);
    }

    [Fact]
    public void SelectChain_HonoursTheConfiguredChainLength()
    {
        // The length counts the whole chain, router included, because that is the number the provider
        // checks: OpenRouter rejects a request naming more than three models outright.
        var roster = Enumerable.Range(0, 10)
            .Select(index => Model($"model/{index:00}", contextLength: 16_000 + index))
            .ToList();

        var chain = ChatModelSelector.SelectChain(
            roster,
            new ChatModelRequirements(),
            Options() with { ChainLength = 3 },
            failurePenalties: null,
            Now);

        chain.Should().HaveCount(3, "two ranked candidates plus the fallback router");
        chain[^1].Should().Be(Fallback);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(8)]
    public void SelectChain_NeverExceedsTheConfiguredLength(int chainLength)
    {
        // A chain one entry longer than configured is exactly the bug that made every answer fail with
        // an unexplained HTTP 400, so the invariant is asserted rather than left to the arithmetic.
        var roster = Enumerable.Range(0, 10)
            .Select(index => Model($"model/{index:00}", contextLength: 16_000 + index))
            .ToList();

        var chain = ChatModelSelector.SelectChain(
            roster,
            new ChatModelRequirements(),
            Options() with { ChainLength = chainLength },
            failurePenalties: null,
            Now);

        // The router is always present, so a length below one still yields a chain of one.
        chain.Should().HaveCount(Math.Max(1, chainLength));
        chain[^1].Should().Be(Fallback);
        chain.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Rank_PlacesPreferredModelsAboveEveryNonPreferredModel()
    {
        // The preference is an operator override: it must win even against a far larger context.
        var options = Options() with { PreferredModelPrefixes = ["google/"] };

        var ranking = ChatModelSelector.Rank(
            [Model("other/huge", contextLength: 1_000_000), Model("google/small", contextLength: 16_000)],
            new ChatModelRequirements(),
            options,
            failurePenalties: null,
            Now);

        ranking[0].Id.Should().Be("google/small");
        ranking[0].IsPreferred.Should().BeTrue();
    }

    [Fact]
    public void Rank_DemotesAModelCarryingAFailurePenalty()
    {
        var roster = new[] { Model("flaky/model", contextLength: 512_000), Model("steady/model", contextLength: 500_000) };

        var withoutPenalty = ChatModelSelector.Rank(roster, new ChatModelRequirements(), Options(), null, Now);
        withoutPenalty[0].Id.Should().Be("flaky/model", "it wins on context before any failures are recorded");

        var withPenalty = ChatModelSelector.Rank(
            roster,
            new ChatModelRequirements(),
            Options(),
            new Dictionary<string, double> { ["flaky/model"] = 0.5d },
            Now);

        withPenalty[0].Id.Should().Be("steady/model");
        withPenalty.Single(candidate => candidate.Id == "flaky/model").FailurePenalty.Should().Be(0.5d);
    }

    [Fact]
    public void Rank_IsStableForModelsThatScoreIdentically()
    {
        // Ties break on id, so repeated calls cannot reshuffle the chain and make behaviour jittery.
        var roster = new[] { Model("b/model", contextLength: 32_000), Model("a/model", contextLength: 32_000) };

        var first = ChatModelSelector.Rank(roster, new ChatModelRequirements(), Options(), null, Now);
        var second = ChatModelSelector.Rank(roster, new ChatModelRequirements(), Options(), null, Now);

        first.Select(candidate => candidate.Id).Should().Equal("a/model", "b/model");
        second.Select(candidate => candidate.Id).Should().Equal(first.Select(candidate => candidate.Id));
    }

    [Fact]
    public void Rank_PrefersAnEstablishedModelOverABrandNewOne()
    {
        // Freshly published free models are frequently capacity-starved or unstable, so they ramp in
        // rather than immediately displacing a model that has been reliable for months.
        var brandNew = Model("new/model", contextLength: 64_000, createdAt: Now.AddDays(-1));
        var established = Model("established/model", contextLength: 64_000, createdAt: Now.AddDays(-60));

        var ranking = ChatModelSelector.Rank(
            [brandNew, established],
            new ChatModelRequirements(),
            Options(),
            failurePenalties: null,
            Now);

        ranking[0].Id.Should().Be("established/model");
    }

    private static ChatModelSelectionOptions Options() => new()
    {
        FallbackModelId = Fallback,
        ChainLength = 3,
        ExpiryHorizon = TimeSpan.FromHours(48)
    };

    private static ChatModelDescriptor Model(
        string id,
        int contextLength,
        bool supportsImageInput = false,
        bool supportsTools = false,
        DateTimeOffset? expiresAt = null,
        DateTimeOffset? createdAt = null) =>
        new(
            id,
            id,
            contextLength,
            MaxCompletionTokens: 8_192,
            supportsImageInput,
            supportsTools,
            SupportsStructuredOutputs: false,
            createdAt ?? Now.AddDays(-60),
            expiresAt);
}
