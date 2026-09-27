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
    public void SelectChain_RanksBiggerContextFirst_AndAppendsTheRouter_WhenCandidatesRunShort()
    {
        var chain = ChatModelSelector.SelectChain(
            [Model("small/model", contextLength: 16_000), Model("big/model", contextLength: 512_000)],
            new ChatModelRequirements(),
            Options(),
            failurePenalties: null,
            Now);

        // Two vetted models cannot fill three slots, and the router fills the one left over.
        chain.Should().Equal("big/model", "small/model", Fallback);
    }

    [Fact]
    public void SelectChain_LeavesTheRouterOut_WhenEnoughVettedModelsQualify()
    {
        // The router picks at random, safety classifiers included: every "User Safety: safe" answer
        // beta testers saw came from it, after the ranked models ahead of it had failed upstream.
        var roster = Enumerable.Range(0, 5)
            .Select(index => Model($"model/{index}", contextLength: 16_000 + index))
            .ToList();

        var chain = ChatModelSelector.SelectChain(roster, new ChatModelRequirements(), Options(), null, Now);

        chain.Should().Equal("model/4", "model/3", "model/2");
    }

    [Fact]
    public void SelectChain_SkipsModelsAlreadyTried_CaseInsensitively()
    {
        var roster = Enumerable.Range(0, 5)
            .Select(index => Model($"model/{index}", contextLength: 16_000 + index))
            .ToList();

        // Built case-sensitively on purpose: the selector must not depend on the caller's comparer.
        var tried = new HashSet<string>(StringComparer.Ordinal) { "MODEL/4", "model/3" };

        var chain = ChatModelSelector.SelectChain(
            roster, new ChatModelRequirements(ExcludedModelIds: tried), Options(), null, Now);

        chain.Should().Equal("model/2", "model/1", "model/0");
    }

    [Fact]
    public void SelectChain_AppendsTheRouter_OnlyOnceTheVettedModelsAreExhausted()
    {
        var roster = new[] { Model("a/model", 64_000), Model("b/model", 32_000), Model("c/model", 16_000) };

        var chain = ChatModelSelector.SelectChain(
            roster,
            new ChatModelRequirements(ExcludedModelIds: new HashSet<string> { "a/model", "b/model" }),
            Options(),
            null,
            Now);

        chain.Should().Equal("c/model", Fallback);
    }

    [Fact]
    public void SelectChain_ReturnsNothing_WhenTheRouterWasTriedAndNothingElseRemains()
    {
        // At most one random pick per question: a retry never goes back to the router.
        var chain = ChatModelSelector.SelectChain(
            [Model("a/model", 64_000)],
            new ChatModelRequirements(ExcludedModelIds: new HashSet<string> { "a/model", Fallback }),
            Options(),
            null,
            Now);

        chain.Should().BeEmpty();
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
    public void SelectChain_ExcludesModelsThatEmitMoreThanText()
    {
        // Modelled on google/lyria-3-pro-preview, a music generator sitting on OpenRouter's roster: it
        // is billed per second of audio, so both token prices read "0" and the free filter admits it,
        // and its 1M context puts it near the top of the ranking. Note it declares text output too —
        // "text and audio" — so the rule has to be text and nothing else, not text among others.
        var musicGenerator = Model("google/lyria-3-pro-preview", contextLength: 1_048_576, producesTextOnly: false);
        var chatModel = Model("real/model", contextLength: 32_000);

        var chain = ChatModelSelector.SelectChain(
            [musicGenerator, chatModel],
            new ChatModelRequirements(),
            Options(),
            failurePenalties: null,
            Now);

        chain.Should().Equal("real/model", Fallback);
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

        chain.Should().HaveCount(3, "three vetted models, and no slot left for the router");
        chain.Should().NotContain(Fallback);
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

        // A length below one still yields a chain of one, and ten candidates leave no slot for the router.
        chain.Should().HaveCount(Math.Max(1, chainLength));
        chain.Should().NotContain(Fallback);
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

    [Fact]
    public void Rank_NeverOffersASafetyClassifier()
    {
        // Given the best context window on the roster it would otherwise win outright. It has to be
        // an eligibility rule rather than something the failure tracker learns: a classifier returns
        // a perfectly valid completion, so the request succeeds, nothing is recorded as a failure,
        // and the model keeps its rank and its turn.
        var ranked = ChatModelSelector.Rank(
            [
                Model("nvidia/nemotron-3.5-content-safety:free", 128_000, isGuardrail: true),
                Model("good/model", 8_192)
            ],
            new ChatModelRequirements(),
            Options(),
            failurePenalties: null,
            Now);

        ranked.Select(candidate => candidate.Id).Should().Equal("good/model");
    }

    [Fact]
    public void InferFailedModels_BlamesTheModelsSkippedBeforeTheOneThatAnswered()
    {
        // The provider tries the chain in order, so a model reached only after others means they failed.
        ChatModelSelector.InferFailedModels(["a/model", "b/model", "c/model"], "c/model", Fallback, [])
            .Should().Equal("a/model", "b/model");
    }

    [Fact]
    public void InferFailedModels_BlamesNobody_WhenTheFirstModelAnswered() =>
        ChatModelSelector.InferFailedModels(["a/model", "b/model"], "A/MODEL", Fallback, [])
            .Should().BeEmpty();

    [Fact]
    public void InferFailedModels_BlamesEveryRankedModel_WhenTheRoutersPickAnswered()
    {
        // An answer from outside the chain is the router's pick, so every model ahead of it failed.
        var roster = new[] { Model("nvidia/nemotron-3.5-content-safety:free", 128_000, isGuardrail: true) };

        ChatModelSelector.InferFailedModels(
                ["a/model", "b/model", Fallback], "nvidia/nemotron-3.5-content-safety:free", Fallback, roster)
            .Should().Equal("a/model", "b/model");
    }

    [Fact]
    public void InferFailedModels_BlamesNobody_ForAnIdItCannotPlace()
    {
        // "a/model" reported back as "a/model-20260901" must not cost "a/model" its rank for answering.
        ChatModelSelector.InferFailedModels(["a/model", "b/model", Fallback], "a/model-20260901", Fallback, [])
            .Should().BeEmpty();
    }

    [Fact]
    public void InferFailedModels_BlamesEveryRankedModel_ButNeverTheRouter_WhenNothingAnswered() =>
        ChatModelSelector.InferFailedModels(["a/model", "b/model", Fallback], answeredBy: null, Fallback, [])
            .Should().Equal("a/model", "b/model");

    [Theory]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    [InlineData(false, true, true)]
    public void IsUnfitToAnswer_FlagsClassifiersGeneratorsAndBlockedModels(bool isGuardrail, bool producesTextOnly, bool blocked)
    {
        var model = Model("odd/model", 64_000, isGuardrail: isGuardrail, producesTextOnly: producesTextOnly);
        var options = blocked
            ? Options() with { BlockedModelIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "odd/model" } }
            : Options();

        ChatModelSelector.IsUnfitToAnswer("odd/model", model, options).Should().BeTrue();
    }

    [Fact]
    public void IsUnfitToAnswer_TrustsAModelTheRosterDoesNotKnow()
    {
        // A stale roster is no reason to throw away an answer from a model we have not heard of yet.
        ChatModelSelector.IsUnfitToAnswer("new/model", model: null, Options()).Should().BeFalse();
        ChatModelSelector.IsUnfitToAnswer("good/model", Model("good/model", 64_000), Options()).Should().BeFalse();
    }

    private static ChatModelDescriptor Model(
        string id,
        int contextLength,
        bool supportsImageInput = false,
        bool supportsTools = false,
        DateTimeOffset? expiresAt = null,
        DateTimeOffset? createdAt = null,
        bool producesTextOnly = true,
        bool isGuardrail = false) =>
        new(
            id,
            id,
            contextLength,
            MaxCompletionTokens: 8_192,
            supportsImageInput,
            producesTextOnly,
            supportsTools,
            SupportsStructuredOutputs: false,
            isGuardrail,
            createdAt ?? Now.AddDays(-60),
            expiresAt);
}
