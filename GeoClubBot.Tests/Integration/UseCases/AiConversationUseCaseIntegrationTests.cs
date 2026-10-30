using FluentAssertions;
using GeoClubBot.Tests.TestBuilders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using UseCases.OutputPorts.AI;
using UseCases.UseCases.AI.Conversations;
using UseCases.UseCases.AI.Retrieval;
using UseCases.UseCases.AI.Tools;
using Utilities;
using Xunit;

namespace GeoClubBot.Tests.Integration.UseCases;

/// <summary>
/// Drives the AI conversation use cases through the real MediatR pipeline against a real database.
///
/// The host auto-substitutes every Application interface under OutputPorts except those named
/// *Repository, so the provider-facing ports are fakes while conversation and budget storage are
/// genuine EF — exactly the split worth testing, since the budget and the reply tree are the parts
/// that must survive real persistence.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class AiConversationUseCaseIntegrationTests(PostgresFixture fixture)
{
    [Fact]
    public async Task AskAi_AnswersAFreshQuestion_AndConsumesBudget()
    {
        using var host = CreateHost();
        ArrangeProviders(host, answer: "Ghanaian bollards are short and white.");

        var messageId = NewSnowflake();
        var result = await host.SendAsync(Ask(messageId, parentId: null, "what do Ghanaian bollards look like?"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Text.Should().Contain("Ghanaian");
        result.Value.ModelUsed.Should().Be("test/model");
        result.Value.ConversationId.Should().Be(messageId, "a fresh question roots its own conversation");
        result.Value.Depth.Should().Be(0);
    }

    [Fact]
    public async Task AskAi_ReplaysPriorTurns_WhenContinuingABranch()
    {
        using var host = CreateHost();
        var chat = ArrangeProviders(host, answer: "Follow-up answer.");

        var (userMessageId, botMessageId, conversationId) = await SeedExchangeAsync(
            host, "what country is this pole from?", "Looks Ghanaian.");

        await host.SendAsync(Ask(NewSnowflake(), parentId: botMessageId, "and the wires?"));

        var request = chat.ReceivedCalls()
            .Select(call => call.GetArguments()[0])
            .OfType<AiChatRequest>()
            .Last();

        var replayed = request.Messages.Select(m => m.ToPlainText()).ToList();
        replayed.Should().Contain(text => text.Contains("what country is this pole from?"));
        replayed.Should().Contain(text => text.Contains("Looks Ghanaian."));
        replayed[^1].Should().Contain("and the wires?");

        conversationId.Should().Be(userMessageId);
    }

    [Fact]
    public async Task AskAi_KeepsSiblingBranchesApart()
    {
        // The behaviour the whole tree design exists for, verified end to end rather than only in the
        // pure builder: two people replying to the same answer must not see each other's follow-ups.
        using var host = CreateHost();
        var chat = ArrangeProviders(host, answer: "answer");

        var (_, botMessageId, _) = await SeedExchangeAsync(host, "original question", "original answer");

        var userA = NewSnowflake();
        var firstBranch = await host.SendAsync(Ask(NewSnowflake(), botMessageId, "branch A question", authorId: userA));
        await RecordAsync(host, firstBranch.Value, botMessageId, "branch A question", userA);

        var userB = NewSnowflake();
        await host.SendAsync(Ask(NewSnowflake(), botMessageId, "branch B question", authorId: userB));

        var lastRequest = chat.ReceivedCalls()
            .Select(call => call.GetArguments()[0])
            .OfType<AiChatRequest>()
            .Last();

        var replayed = string.Join("\n", lastRequest.Messages.Select(m => m.ToPlainText()));
        replayed.Should().Contain("original question", "the shared prefix is common to both branches");
        replayed.Should().NotContain("branch A question", "the sibling branch must stay invisible");
    }

    [Fact]
    public async Task AskAi_RefusesOnceTheDailyBudgetIsSpent()
    {
        // The budget is deliberately global per UTC day, so every other test in this class has already
        // spent against today's row in the shared container. Reset it to isolate this assertion.
        await ResetTodaysBudgetAsync();

        // One question costs two upstream calls, so a budget of two allows exactly one question.
        using var host = CreateHost(dailyBudget: 2);
        ArrangeProviders(host, answer: "answer");

        (await host.SendAsync(Ask(NewSnowflake(), null, "first"))).IsSuccess.Should().BeTrue();

        var second = await host.SendAsync(Ask(NewSnowflake(), null, "second"));

        second.IsFailure.Should().BeTrue();
        second.Error.Code.Should().Be("ai.budget_exhausted");
    }

    [Fact]
    public async Task AskAi_ThrottlesASingleUser_WithoutBlockingEveryoneElse()
    {
        using var host = CreateHost(perUserPerHour: 1);
        ArrangeProviders(host, answer: "answer");

        var heavyUser = NewSnowflake();
        var first = await host.SendAsync(Ask(NewSnowflake(), null, "first", authorId: heavyUser));
        await RecordAsync(host, first.Value, parentId: null, "first", heavyUser);

        var throttled = await host.SendAsync(Ask(NewSnowflake(), null, "second", authorId: heavyUser));
        throttled.IsFailure.Should().BeTrue();
        throttled.Error.Code.Should().Be("ai.user_throttled");

        var otherUser = await host.SendAsync(Ask(NewSnowflake(), null, "mine", authorId: NewSnowflake()));
        otherUser.IsSuccess.Should().BeTrue("the cap is per user, not global");
    }

    [Fact]
    public async Task AskAi_RefusesRatherThanGuess_WhenRetrievalIsUnavailable()
    {
        // Observed in production: a transient embedder failure produced an empty hit list, the prompt
        // reported "No guide excerpts matched this question", and the model told the user in good
        // faith that the guides do not cover a road the guides document in detail. A retrievalless
        // answer that claims to have checked is worse than no answer, and it spends the chat request
        // to produce it, so the failure is surfaced instead.
        using var host = CreateHost();
        var chat = ArrangeProviders(host, answer: "From general knowledge.");
        host.Mock<IEmbedder>().EmbedAsync(Arg.Any<IReadOnlyList<EmbeddingInput>>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<ReadOnlyMemory<float>>>.Failure(
                Error.Unexpected("ai.embedding_failed", "down")));

        var result = await host.SendAsync(Ask(NewSnowflake(), null, "question"));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("ai.embedding_failed");
        chat.ReceivedCalls().Should().BeEmpty("the chat request must not be spent on an ungrounded answer");
    }

    [Fact]
    public async Task AskAi_CreditsTheRetrievedGuides_WhenTheModelCitesNothing()
    {
        // Free models comply with the citation instruction inconsistently — the same question cites on
        // one run and not the next — so attribution cannot rest on the prompt alone.
        using var host = CreateHost();
        ArrangeProviders(host, answer: "Roads starting with MR are exclusive to Eswatini.");
        host.Mock<IKnowledgeIndex>().SearchAsync(Arg.Any<KnowledgeQuery>(), Arg.Any<CancellationToken>())
            .Returns([Hit("Roads starting with MR…", "https://www.plonkit.net/eswatini#m1jr")]);

        var result = await host.SendAsync(Ask(NewSnowflake(), null, "where are MR roads?"));

        result.Value.Sources.Should().ContainSingle();
        result.Value.Sources[0].Url.Should().Be("https://www.plonkit.net/eswatini#m1jr");
        result.Value.Sources[0].Marker.Should().BeNull(
            "there is no marker in the prose for a number to anchor to");
    }

    [Fact]
    public async Task AskAi_CreditsEachSectionOfOneDocument_AsALineOfItsOwn_WhenTheModelCitesNothing()
    {
        // Credited as they would have been cited: one line per section, not one per link.
        using var host = CreateHost();
        ArrangeProviders(host, answer: "Front plates are required in most states.");
        host.Mock<IKnowledgeIndex>().SearchAsync(Arg.Any<KnowledgeQuery>(), Arg.Any<CancellationToken>())
            .Returns(
            [
                Section("Most states require a front plate.", "A Unique USA > Front Plate Requirements"),
                Section("Ohio dropped its front plate.", "A Unique USA > Front Plate Requirements"),
                Section("Counties are listed by state.", "A Unique USA > Counties (33) ->")
            ]);

        var result = await host.SendAsync(Ask(NewSnowflake(), null, "which states don't require front plates?"));

        result.Value.Sources.Select(source => (source.Marker, source.Label)).Should().Equal(
            (null, "USA · A Unique USA > Front Plate Requirements"),
            (null, "USA · A Unique USA > Counties (33)"));

        static KnowledgeHit Section(string text, string section) =>
            new(Guid.NewGuid(), 0.9f, KnowledgeChunkKind.Text, text, "https://docs.google.com/document/d/usa/edit", ImageUrl: null,
                "A Unique USA", "usa", section, null, 0);
    }

    [Fact]
    public async Task AskAi_TitlesAPictureWithoutATitle_AsTheSourceListNamesIt()
    {
        // Every PlonkIt picture comes without a title; its embed read "Guide image" (2026-10-29).
        using var host = CreateHost();
        ArrangeProviders(host, answer: "Rounded tiles are common in central India [1].");
        host.Mock<IKnowledgeIndex>().SearchAsync(Arg.Any<KnowledgeQuery>(), Arg.Any<CancellationToken>())
            .Returns(
            [
                new KnowledgeHit(Guid.NewGuid(), 0.8f, KnowledgeChunkKind.Image, "Rounded roof tiles are common in central India.",
                    "https://www.plonkit.net/india#cIYN", "https://relay/tiles.png",
                    null, "india", "India > Regional and state-specific clues", "Plonk It team", 0)
            ]);

        var result = await host.SendAsync(Ask(NewSnowflake(), null, "where are round roof tiles in India?"));

        result.Value.Images.Should().ContainSingle().Which.Title.Should().Be("India > Regional and state-specific clues");
        result.Value.Sources.Should().ContainSingle().Which.Label.Should().Be("India > Regional and state-specific clues");
    }

    [Fact]
    public async Task AskAi_OffersWhereAPlantIsRecorded_BeforeTheGuides_WhenTheQuestionAsksWhereItIsFound()
    {
        // #25 and #49 of the 2026-10-23 export: offered two guides on India and Vietnam, the answer missed
        // the other countries, and the model never called the species tool to find them.
        using var host = CreateHost();
        var chat = ArrangeProviders(host, answer: "Khasi pines are recorded most in the Philippines, China and Thailand [1].");
        host.Mock<IKnowledgeIndex>().SearchAsync(Arg.Any<KnowledgeQuery>(), Arg.Any<CancellationToken>())
            .Returns([Hit("Khasi pines grow in Meghalaya.", "https://www.plonkit.net/india#pine")]);
        var species = host.Mock<ISpeciesRangeReader>();
        species.ReadAsync(Arg.Any<SpeciesRangeQuery>(), Arg.Any<CancellationToken>())
            .Returns(Error.NotFound("ai.species_not_found", "No such plant."));
        species.ReadAsync(new SpeciesRangeQuery("Khasi pine", null), Arg.Any<CancellationToken>())
            .Returns(new SpeciesRange(
                5285824, "Pinus kesiya Royle ex Gordon", "Pinus kesiya", "SPECIES", MatchedByCommonName: true, FuzzyMatch: false,
                CountryCode: null, [new("PH", 256), new("CN", 105), new("TH", 87), new("IN", 80)], 1061));

        var before = await ReadTodaysBudgetAsync();
        var result = await host.SendAsync(Ask(NewSnowflake(), null, "where can I find Khasi pines?"));
        var after = await ReadTodaysBudgetAsync();

        var prompt = string.Join('\n', chat.ReceivedCalls().Select(call => call.GetArguments()[0]).OfType<AiChatRequest>()
            .Single().Messages.Select(message => message.ToPlainText()));
        prompt.Should().Contain("[1] Where Pinus kesiya is recorded (GBIF)").And.Contain("[2] ");
        result.Value.Sources.Should().ContainSingle().Which.Url.Should().Be("https://www.gbif.org/species/5285824");
        (after.Requests - before.Requests).Should().Be(2, "GBIF costs no AI request");
    }

    [Fact]
    public async Task AskAi_KeepsTheModelsOwnCitations_WhenItDoesCite()
    {
        using var host = CreateHost();
        ArrangeProviders(host, answer: "Roads starting with MR are exclusive to Eswatini [1].");
        host.Mock<IKnowledgeIndex>().SearchAsync(Arg.Any<KnowledgeQuery>(), Arg.Any<CancellationToken>())
            .Returns([Hit("Roads starting with MR…", "https://www.plonkit.net/eswatini#m1jr")]);

        var result = await host.SendAsync(Ask(NewSnowflake(), null, "where are MR roads?"));

        result.Value.Sources.Should().ContainSingle();
        result.Value.Sources[0].Marker.Should().Be(1, "the number has to match the one in the prose");
    }

    [Fact]
    public async Task AskAi_CreditsNothing_WhenRetrievalFoundNothingToCredit()
    {
        // The fallback credits guides that were actually retrieved. With none, inventing an
        // attribution would be worse than leaving the answer bare.
        using var host = CreateHost();
        ArrangeProviders(host, answer: "From general knowledge.");
        host.Mock<IKnowledgeIndex>().SearchAsync(Arg.Any<KnowledgeQuery>(), Arg.Any<CancellationToken>())
            .Returns([]);

        var result = await host.SendAsync(Ask(NewSnowflake(), null, "something obscure"));

        result.Value.Sources.Should().BeEmpty();
    }

    [Fact]
    public async Task AskAi_SendsNoImageVector_WhenNoImageWasAttached()
    {
        // Every text question failed against the real store with "expected dim: 2048, got 0", because
        // `count > 1 ? vectors[1] : null` has ReadOnlyMemory<float> as its natural type — the null
        // literal converts to an array, and an array converts to an empty memory. The query therefore
        // carried an image vector of length zero instead of none, and the store rejected all of it.
        using var host = CreateHost();
        ArrangeProviders(host, answer: "An answer.");
        var index = host.Mock<IKnowledgeIndex>();

        await host.SendAsync(Ask(NewSnowflake(), null, "a question with no attachment"));

        var query = index.ReceivedCalls().Select(c => c.GetArguments()[0]).OfType<KnowledgeQuery>().Last();
        query.ImageVector.Should().BeNull("nothing was attached, so there is no image to search by");
        query.TextVector.Should().NotBeNull();
    }

    [Fact]
    public async Task AskAi_StillAnswers_WhenRetrievalSimplyFoundNothing()
    {
        // The other half of the distinction: a lookup that genuinely matched nothing is a real answer
        // about the corpus, and the model is told so plainly rather than left to invent guide content.
        using var host = CreateHost();
        var chat = ArrangeProviders(host, answer: "From general knowledge.");
        host.Mock<IKnowledgeIndex>().SearchAsync(Arg.Any<KnowledgeQuery>(), Arg.Any<CancellationToken>())
            .Returns([]);

        var result = await host.SendAsync(Ask(NewSnowflake(), null, "question"));

        result.IsSuccess.Should().BeTrue();
        var request = chat.ReceivedCalls().Select(c => c.GetArguments()[0]).OfType<AiChatRequest>().Last();
        request.Messages[^1].ToPlainText().Should().Contain("No guide excerpts matched");
    }

    [Fact]
    public async Task AskAi_AsksForAVisionModel_OnlyWhenAnImageIsAttached()
    {
        using var host = CreateHost();
        ArrangeProviders(host, answer: "answer");
        var catalog = host.Mock<IChatModelCatalog>();

        await host.SendAsync(Ask(NewSnowflake(), null, "no image here"));
        await catalog.Received().ReadChainAsync(
            Arg.Is<ChatModelRequirements>(r => !r.NeedsImageInput), Arg.Any<CancellationToken>());

        await host.SendAsync(Ask(NewSnowflake(), null, "what is this?", images: ["https://cdn/x.png"]));
        await catalog.Received().ReadChainAsync(
            Arg.Is<ChatModelRequirements>(r => r.NeedsImageInput), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AskAi_AsksForAVisionModel_WhenTheReplayedHistoryCarriesAnImage()
    {
        // "How can I tell this one is Bengali?" attaches nothing, but replays the screenshot it is
        // about — a text-only model asked it cannot see what "this one" is.
        using var host = CreateHost();
        ArrangeProviders(host, answer: "answer");
        var catalog = host.Mock<IChatModelCatalog>();

        var rootId = NewSnowflake();
        var first = await host.SendAsync(Ask(rootId, null, "is this Bengali?", images: ["https://cdn/sign.png"]));
        var botMessageId = await RecordAsync(host, first.Value, null, "is this Bengali?", userId: 55, rootId,
            images: ["https://cdn/sign.png"]);
        catalog.ClearReceivedCalls();

        await host.SendAsync(Ask(NewSnowflake(), botMessageId, "how can you tell?", authorId: 55));

        await catalog.Received().ReadChainAsync(
            Arg.Is<ChatModelRequirements>(r => r.NeedsImageInput), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AskAi_RetriesWithOtherModels_WhenTheAnswerIsASafetyVerdict()
    {
        // Seen three times in the beta-test feedback: the answer was literally "User Safety: safe".
        // A classifier's verdict is a valid completion, so nothing noticed it until a reader did.
        using var host = CreateHost();
        ArrangeProviders(host, answer: "unused");
        var catalog = ArrangeChains(host, first: ["a/model", "b/model", "c/model"], retry: ["d/model"]);
        ArrangeCompletions(host,
            Success("User Safety: safe", "a/model"),
            Success("Red soil is common across Malaysia.", "d/model"));

        var before = await ReadTodaysBudgetAsync();
        var result = await host.SendAsync(Ask(NewSnowflake(), null, "is red soil in malaysia regional"));
        var after = await ReadTodaysBudgetAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.Text.Should().Be("Red soil is common across Malaysia.");
        result.Value.ModelUsed.Should().Be("d/model");

        catalog.Received(1).ReportUnfit("a/model");
        // Only the model that gave the verdict was reached; the two behind it are still worth asking.
        await catalog.Received().ReadChainAsync(
            Arg.Is<ChatModelRequirements>(r => r.ExcludedModelIds != null
                                               && r.ExcludedModelIds.Contains("a/model")
                                               && !r.ExcludedModelIds.Contains("b/model")),
            Arg.Any<CancellationToken>());

        (after.Requests - before.Requests).Should().Be(3, "the embedding, the first chain and the retry");
        (after.PromptTokens - before.PromptTokens).Should().Be(20, "the discarded completion was billed too");
    }

    [Fact]
    public async Task AskAi_Retries_WhenTheRouterPickedAModelUnfitToAnswer()
    {
        // Without tools: the router only ever serves a question asked in one call. A chain for tools
        // never names it (AskAi_NeverOffersToolsThroughTheRouter).
        using var host = CreateHost(tools: false);
        ArrangeProviders(host, answer: "unused");
        var catalog = ArrangeChains(host, first: ["a/model", "openrouter/free"], retry: ["d/model"]);
        catalog.IsUnfitToAnswer("nvidia/nemotron-3.5-content-safety:free").Returns(true);
        ArrangeCompletions(host,
            Success("I can only classify content.", "nvidia/nemotron-3.5-content-safety:free"),
            Success("Look at the poles.", "d/model"));

        var result = await host.SendAsync(Ask(NewSnowflake(), null, "where is this?"));

        result.Value.Text.Should().Be("Look at the poles.");
        catalog.DidNotReceive().ReportUnfit(Arg.Any<string>());
        // Answered from outside the chain, so every entry of the chain was tried — the router included.
        await catalog.Received().ReadChainAsync(
            Arg.Is<ChatModelRequirements>(r => r.ExcludedModelIds != null
                                               && r.ExcludedModelIds.Contains("a/model")
                                               && r.ExcludedModelIds.Contains("openrouter/free")
                                               && r.ExcludedModelIds.Contains("nvidia/nemotron-3.5-content-safety:free")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AskAi_RetriesWithModelsNotYetTried_AfterAQuickFailure()
    {
        using var host = CreateHost();
        ArrangeProviders(host, answer: "unused");
        var catalog = ArrangeChains(host, first: ["a/model", "b/model"], retry: ["c/model"]);
        ArrangeCompletions(host,
            Failure(ChatErrorCodes.RequestFailed),
            Success("Ghanaian bollards are white.", "c/model"));

        var result = await host.SendAsync(Ask(NewSnowflake(), null, "bollards?"));

        result.Value.Text.Should().Be("Ghanaian bollards are white.");
        catalog.Received().ReportChainOutcome(
            Arg.Is<IReadOnlyList<string>>(chain => chain.SequenceEqual(new[] { "a/model", "b/model" })), null);
        await catalog.Received().ReadChainAsync(
            Arg.Is<ChatModelRequirements>(r => r.ExcludedModelIds != null
                                               && r.ExcludedModelIds.Contains("a/model")
                                               && r.ExcludedModelIds.Contains("b/model")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AskAi_ExcludesAGatedFirstModel_AndRetriesWithOthers()
    {
        // inkling on 2026-10-23: 403 "only available on agentic harnesses" to anything but a coding agent.
        // A plain 403 is a request refused as sent and is never retried; this one is the model's refusal,
        // the next models answer the same request, and this one never will.
        using var host = CreateHost();
        ArrangeProviders(host, answer: "unused");
        var catalog = ArrangeChains(host, first: ["gated/model", "b/model"], retry: ["c/model"]);
        ArrangeCompletions(host,
            Failure(ChatErrorCodes.ModelGated),
            Success("Bollards in Ghana are white.", "c/model"));

        var before = await ReadTodaysBudgetAsync();
        var result = await host.SendAsync(Ask(NewSnowflake(), null, "bollards?"));
        var after = await ReadTodaysBudgetAsync();

        result.Value.Text.Should().Be("Bollards in Ghana are white.");
        catalog.Received(1).ReportUnfit("gated/model");
        catalog.DidNotReceive().ReportUnfit("b/model");
        catalog.DidNotReceive().ReportChainOutcome(Arg.Any<IReadOnlyList<string>>(), null);
        await catalog.Received().ReadChainAsync(
            Arg.Is<ChatModelRequirements>(r => r.ExcludedModelIds != null
                                               && r.ExcludedModelIds.Contains("gated/model")
                                               && r.ExcludedModelIds.Contains("b/model")),
            Arg.Any<CancellationToken>());
        (after.Requests - before.Requests).Should().Be(3, "the embedding, the first chain and the retry");
    }

    [Fact]
    public async Task AskAi_DemotesOnlyABusyFirstModel_AndRetriesWithOthers()
    {
        // gemma on 2026-10-23: a 429 from its provider's pool shared by every free user, not from our key.
        // Our allowance is fine and the other models are not to blame, so only the head is demoted.
        using var host = CreateHost();
        ArrangeProviders(host, answer: "unused");
        var catalog = ArrangeChains(host, first: ["busy/model", "b/model"], retry: ["c/model"]);
        ArrangeCompletions(host,
            Failure(ChatErrorCodes.ModelRateLimited),
            Success("Bollards in Ghana are white.", "c/model"));

        var result = await host.SendAsync(Ask(NewSnowflake(), null, "bollards?"));

        result.Value.Text.Should().Be("Bollards in Ghana are white.");
        catalog.Received(1).ReportFailure("busy/model");
        catalog.DidNotReceive().ReportFailure("b/model");
        catalog.DidNotReceive().ReportUnfit(Arg.Any<string>());
        catalog.DidNotReceive().ReportChainOutcome(Arg.Any<IReadOnlyList<string>>(), null);
    }

    [Theory]
    [InlineData(ChatErrorCodes.RateLimited)]
    [InlineData(ChatErrorCodes.Unreachable)]
    [InlineData(ChatErrorCodes.Rejected)]
    public async Task AskAi_DoesNotRetry_WhenOtherModelsWouldFailTheSameWay(string code)
    {
        // Every model sits behind the same provider, the same key and the same request; none of these
        // is the models' fault.
        using var host = CreateHost();
        ArrangeProviders(host, answer: "unused");
        var catalog = ArrangeChains(host, first: ["a/model", "b/model"], retry: ["c/model"]);
        var chat = ArrangeCompletions(host, Failure(code), Success("never asked", "c/model"));

        var result = await host.SendAsync(Ask(NewSnowflake(), null, "bollards?"));

        result.Error.Code.Should().Be(code);
        await chat.Received(1).CompleteAsync(Arg.Any<AiChatRequest>(), Arg.Any<CancellationToken>());
        catalog.DidNotReceive().ReportChainOutcome(Arg.Any<IReadOnlyList<string>>(), Arg.Any<string?>());
    }

    [Fact]
    public async Task AskAi_GivesUp_WhenTheRetryWouldExceedTheBudget()
    {
        await ResetTodaysBudgetAsync();

        // Two requests cover the embedding and the first chain, and nothing more.
        using var host = CreateHost(dailyBudget: 2);
        ArrangeProviders(host, answer: "unused");
        ArrangeChains(host, first: ["a/model"], retry: ["b/model"]);
        var chat = ArrangeCompletions(host, Success("User Safety: safe", "a/model"), Success("never asked", "b/model"));

        var result = await host.SendAsync(Ask(NewSnowflake(), null, "question"));

        result.Error.Code.Should().Be(ChatErrorCodes.UnusableAnswer);
        await chat.Received(1).CompleteAsync(Arg.Any<AiChatRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AskAi_GivesUp_WithoutSpending_WhenNoUntriedModelRemains()
    {
        using var host = CreateHost();
        ArrangeProviders(host, answer: "unused");
        ArrangeChains(host, first: ["a/model"], retry: []);
        var chat = ArrangeCompletions(host, Failure(ChatErrorCodes.RequestFailed), Success("never asked", "b/model"));

        var before = await ReadTodaysBudgetAsync();
        var result = await host.SendAsync(Ask(NewSnowflake(), null, "question"));
        var after = await ReadTodaysBudgetAsync();

        result.Error.Code.Should().Be(ChatErrorCodes.RequestFailed);
        await chat.Received(1).CompleteAsync(Arg.Any<AiChatRequest>(), Arg.Any<CancellationToken>());
        (after.Requests - before.Requests).Should().Be(2, "a retry with nobody left to ask costs nothing");
    }

    [Fact]
    public async Task AskAi_RetriesAtMostOnce()
    {
        using var host = CreateHost();
        ArrangeProviders(host, answer: "unused");
        ArrangeChains(host, first: ["a/model"], retry: ["b/model"]);
        var chat = ArrangeCompletions(host,
            Success("User Safety: safe", "a/model"),
            Success("Response Safety: safe", "b/model"),
            Success("never asked", "c/model"));

        var result = await host.SendAsync(Ask(NewSnowflake(), null, "question"));

        result.Error.Code.Should().Be(ChatErrorCodes.UnusableAnswer);
        await chat.Received(2).CompleteAsync(Arg.Any<AiChatRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AskAi_ReportsWhichModelsTheProviderSkipped()
    {
        using var host = CreateHost();
        ArrangeProviders(host, answer: "unused");
        var catalog = ArrangeChains(host, first: ["a/model", "b/model", "c/model"], retry: []);
        ArrangeCompletions(host, Success("An answer.", "b/model"));

        await host.SendAsync(Ask(NewSnowflake(), null, "question"));

        catalog.Received(1).ReportChainOutcome(
            Arg.Is<IReadOnlyList<string>>(chain => chain.SequenceEqual(new[] { "a/model", "b/model", "c/model" })),
            "b/model");
    }

    [Fact]
    public async Task AskAi_CountsPictureCitationsAmongTheCitedSources()
    {
        // An answer that cited only pictures used to be archived as citing nothing at all.
        using var host = CreateHost();
        ArrangeProviders(host, answer: "Thai plates carry two letters [2].");
        host.Mock<IKnowledgeIndex>().SearchAsync(Arg.Any<KnowledgeQuery>(), Arg.Any<CancellationToken>())
            .Returns(
            [
                Hit("Thai taxis are pink.", "https://www.plonkit.net/thailand#taxi"),
                new KnowledgeHit(Guid.NewGuid(), 0.8f, KnowledgeChunkKind.Image, "Thai licence plates",
                    "https://www.plonkit.net/thailand#plates", "https://relay/plates.png",
                    "Thailand", "thailand", "Thailand > Plates", "Plonk It team", 0)
            ]);

        var result = await host.SendAsync(Ask(NewSnowflake(), null, "what do Thai plates look like?"));

        result.Value.Text.Should().Be("Thai plates carry two letters [1].");
        result.Value.Images.Should().ContainSingle().Which.Marker.Should().Be(1);
        result.Value.Sources.Should().ContainSingle().Which.IsImage.Should().BeTrue();
        result.Value.CitedSourceUrls.Should().Equal("https://www.plonkit.net/thailand#plates");
    }

    [Fact]
    public async Task RecordAiTurns_LinksTheAnswerToTheQuestion()
    {
        using var host = CreateHost();
        ArrangeProviders(host, answer: "an answer");

        var userMessageId = NewSnowflake();
        var answer = await host.SendAsync(Ask(userMessageId, null, "a question"));
        var botMessageId = await RecordAsync(host, answer.Value, null, "a question", userId: 55, userMessageId);

        await using var db = fixture.CreateDbContext();
        var repository = new Infrastructure.OutputAdapters.Repositories.EfAiConversationRepository(db);

        var stored = await repository.ReadConversationAsync(answer.Value.ConversationId);
        stored.Should().HaveCount(2);

        var assistant = stored.Single(t => t.DiscordMessageId == botMessageId);
        assistant.ParentDiscordMessageId.Should().Be(userMessageId, "the answer replies to the question");
        assistant.Depth.Should().Be(1);
        assistant.ModelId.Should().Be("test/model");
    }

    [Fact]
    public async Task AskAi_KeepsAWholeThreadUnderOneRoot_AcrossSeveralExchanges()
    {
        // Rooting a continuation at the message being replied to holds only for the first follow-up.
        // Past that it re-roots the tree every exchange, so the conversation is stored as a chain of
        // two-turn fragments: the third question replays only the second, and anything that later
        // walks the branch sees only the last pair.
        using var host = CreateHost();
        var chat = ArrangeProviders(host, answer: "answer");

        var (rootMessageId, firstBotId, conversationId) = await SeedExchangeAsync(
            host, "what country is this pole from?", "Looks Ghanaian.");

        conversationId.Should().Be(rootMessageId);

        var secondUserId = NewSnowflake();
        var second = await host.SendAsync(Ask(secondUserId, firstBotId, "and the wires?", authorId: 55));
        second.Value.ConversationId.Should().Be(rootMessageId);

        var secondBotId = await RecordAsync(
            host, second.Value, firstBotId, "and the wires?", userId: 55, secondUserId);

        var third = await host.SendAsync(Ask(NewSnowflake(), secondBotId, "and the bollards?", authorId: 55));

        third.Value.ConversationId.Should().Be(rootMessageId, "every turn of a thread shares its root");
        // Depth of the incoming question: 0 root, 1 first answer, 2 second question, 3 second answer.
        third.Value.Depth.Should().Be(4, "depth keeps counting across exchanges even when the root did not");

        var replayed = chat.ReceivedCalls()
            .Select(call => call.GetArguments()[0])
            .OfType<AiChatRequest>()
            .Last()
            .Messages
            .Select(message => message.ToPlainText())
            .ToList();

        replayed.Should().Contain(text => text.Contains("what country is this pole from?"),
            "the third question must still see the first");
        replayed.Should().Contain(text => text.Contains("and the wires?"));

        await using var db = fixture.CreateDbContext();
        var stored = await db.AiConversationTurns
            .Where(turn => turn.ConversationId == rootMessageId)
            .CountAsync();

        stored.Should().Be(4, "all four stored turns belong to one conversation");
    }

    [Fact]
    public async Task AskAi_SearchesAFollowUpByItsBranch_InTheSameEmbeddingRequest()
    {
        // "What about corn?" found the Philippine corn tip 10th by its own words and 1st with the
        // question before it. Both are searched, and embedded together: a follow-up costs no more
        // requests than a fresh question.
        using var host = CreateHost();
        ArrangeProviders(host, answer: "answer");
        var embedder = host.Mock<IEmbedder>();

        var (_, botMessageId, _) = await SeedExchangeAsync(host, "where can I find sugar cane in the Philippines?", "On Negros.");
        embedder.ClearReceivedCalls();
        var before = await ReadTodaysBudgetAsync();

        await host.SendAsync(Ask(NewSnowflake(), botMessageId, "What about corn?"));

        var after = await ReadTodaysBudgetAsync();
        await embedder.Received(1).EmbedAsync(Arg.Any<IReadOnlyList<EmbeddingInput>>(), Arg.Any<CancellationToken>());
        EmbeddedTexts(host).Should().Equal(
            "where can I find sugar cane in the Philippines? What about corn?",
            "What about corn?");

        var query = LastQuery(host);
        query.ExtraTextVectors.Should().ContainSingle().Which.Weight.Should().BeLessThan(query.TextWeight,
            "a follow-up's own words often match nothing on its topic");
        (after.Requests - before.Requests).Should().Be(2, "one embedding and one answer, as for a fresh question");
    }

    [Fact]
    public async Task AskAi_OffersAFollowUpWhatThePreviousAnswerCited_First()
    {
        // "Can you please show me an image of the exact distribution?" was answered "I don't have the
        // actual distribution image": its own search offered one Mongolian excerpt of eight, not the map
        // the answer before had cited as [1]. Now that answer's cited excerpts lead the follow-up's.
        using var host = CreateHost();
        ArrangeProviders(host, answer: "unused");
        var index = host.Mock<IKnowledgeIndex>();

        var copyId = Guid.NewGuid();
        var map = new KnowledgeHit(Guid.NewGuid(), 0.9f, KnowledgeChunkKind.Image, "Four straps near Khorgo.",
            "https://docs.google.com/document/d/mongolia", "https://relay/map.png", "Mongolia Gen 4 Car Meta", "mongolia",
            "Mongolia Gen 4 Car Meta", null, 0, "gdoc", "mongolia", "p1")
        {
            ExtraImageUrls = ["https://relay/map-2.png"],
            MergedChunkIds = [copyId]
        };
        var tents = Hit("Open tents bridge the strap sections.", "https://www.plonkit.net/mongolia#t1");
        index.SearchAsync(Arg.Any<KnowledgeQuery>(), Arg.Any<CancellationToken>()).Returns([tents, map], [Hit("Mongolian plates.", "https://www.plonkit.net/mongolia#p"), map]);
        index.ReadChunksAsync(Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>()).Returns([map with { Score = 0f }]);
        var chat = ArrangeCompletions(host,
            Success("The four-band car is found around Khorgo [2].", "test/model"),
            Success("Here is the map [1].", "test/model"));

        var firstMessageId = NewSnowflake();
        var first = await host.SendAsync(Ask(firstMessageId, null, "where does the four band car happen in Gen 4 Mongolia?"));

        first.Value.CitedChunkIds.Should().Equal([map.Id, copyId], "every copy merged into the cited excerpt");
        first.Value.OfferedChunkIds.Should().Equal(tents.Id, map.Id, copyId);
        await index.DidNotReceive().ReadChunksAsync(Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>());

        var botMessageId = await RecordAsync(host, first.Value, null, "where does the four band car happen?", userId: 55, firstMessageId);

        var followUp = await host.SendAsync(Ask(NewSnowflake(), botMessageId, "Can you please show me an image of the exact distribution?"));

        await index.Received(1).ReadChunksAsync(
            Arg.Is<IReadOnlyList<Guid>>(ids => ids.SequenceEqual(new[] { map.Id, copyId })), Arg.Any<CancellationToken>());

        var prompt = chat.ReceivedCalls().Select(call => call.GetArguments()[0]).OfType<AiChatRequest>().Last().Messages[^1].ToPlainText();
        prompt.Should().Contain("([1] is the excerpt your previous answer cited; the rest were found for this question.)");
        prompt.Should().Contain("[1] (2 pictures) Mongolia").And.Contain("[2] Eswatini > Identifying").And.NotContain("[3]",
            "the new search found the map again, and it is offered once, under its carried number");

        followUp.Value.Text.Should().Be("Here is the map [1].");
        followUp.Value.Images.Select(image => image.ImageUrl).Should().Equal("https://relay/map.png", "https://relay/map-2.png");
        followUp.Value.CitedChunkIds.Should().Equal(map.Id, copyId);

        await using var db = fixture.CreateDbContext();
        var stored = await db.AiConversationTurns.AsNoTracking().SingleAsync(turn => turn.DiscordMessageId == botMessageId);
        stored.CitedChunkIds.Should().Equal(map.Id, copyId);
        stored.OfferedChunkIds.Should().Equal(tents.Id, map.Id, copyId);
    }

    [Fact]
    public async Task AskAi_AnswersAFollowUpFromItsOwnSearch_WhenTheCitedExcerptsCannotBeRead()
    {
        using var host = CreateHost();
        ArrangeProviders(host, answer: "From the search.");
        var index = host.Mock<IKnowledgeIndex>();
        var cited = Hit("Sabah lamp posts.", "https://www.plonkit.net/malaysia#s");
        index.SearchAsync(Arg.Any<KnowledgeQuery>(), Arg.Any<CancellationToken>()).Returns([cited]);
        index.ReadChunksAsync(Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IReadOnlyList<KnowledgeHit>>(new InvalidOperationException("store down")));
        var chat = ArrangeCompletions(host, Success("Mainland lamps [1].", "test/model"), Success("From the search [1].", "test/model"));

        var messageId = NewSnowflake();
        var first = await host.SendAsync(Ask(messageId, null, "mainland lamps in Sabah?"));
        var botMessageId = await RecordAsync(host, first.Value, null, "mainland lamps in Sabah?", userId: 55, messageId);

        var followUp = await host.SendAsync(Ask(NewSnowflake(), botMessageId, "How can I clearly identify Sabah?"));

        followUp.IsSuccess.Should().BeTrue("the excerpts carried forward improve a follow-up; they are never a reason to fail it");
        chat.ReceivedCalls().Select(call => call.GetArguments()[0]).OfType<AiChatRequest>().Last()
            .Messages[^1].ToPlainText().Should().NotContain("previous answer cited");
    }

    [Fact]
    public async Task AskAi_CarriesNothing_WhenCarryingIsTurnedOff()
    {
        using var host = CreateHost(maxCarried: 0);
        ArrangeProviders(host, answer: "unused");
        var index = host.Mock<IKnowledgeIndex>();
        index.SearchAsync(Arg.Any<KnowledgeQuery>(), Arg.Any<CancellationToken>()).Returns([Hit("Sabah lamp posts.", "https://www.plonkit.net/malaysia#s")]);
        ArrangeCompletions(host, Success("Mainland lamps [1].", "test/model"), Success("Sabah [1].", "test/model"));

        var messageId = NewSnowflake();
        var first = await host.SendAsync(Ask(messageId, null, "mainland lamps in Sabah?"));
        var botMessageId = await RecordAsync(host, first.Value, null, "mainland lamps in Sabah?", userId: 55, messageId);
        await host.SendAsync(Ask(NewSnowflake(), botMessageId, "and Sarawak?"));

        await index.DidNotReceive().ReadChunksAsync(Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AskAi_TellsTheModelWhichNumberTheReaderSawEachCarriedExcerptUnder()
    {
        // Both chunks of the Doc were listed to the reader as [1] and the map as [2]; carried, the map is
        // [3]. "Can you show [2] bigger?" has to lead the model to the map.
        using var host = CreateHost();
        ArrangeProviders(host, answer: "unused");
        var index = host.Mock<IKnowledgeIndex>();

        var chunkA = Hit("Four straps near Khorgo.", "https://docs.google.com/document/d/mongolia");
        var chunkB = Hit("Three straps further east.", "https://docs.google.com/document/d/mongolia");
        var map = new KnowledgeHit(Guid.NewGuid(), 0.9f, KnowledgeChunkKind.Image, "Strap map.",
            "https://www.plonkit.net/mongolia#m", "https://relay/map.png", "Mongolia", "mongolia", "Mongolia > Car metas", null, 0);
        index.SearchAsync(Arg.Any<KnowledgeQuery>(), Arg.Any<CancellationToken>()).Returns([chunkA, chunkB, map], []);
        index.ReadChunksAsync(Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>()).Returns([chunkA, chunkB, map]);
        var chat = ArrangeCompletions(host,
            Success("Four straps [1], three further east [2], see the map [3].", "test/model"),
            Success("Here it is [3].", "test/model"));

        var messageId = NewSnowflake();
        var first = await host.SendAsync(Ask(messageId, null, "where are the strap cars?"));
        first.Value.Text.Should().Be("Four straps [1], three further east [1], see the map [2].");
        var botMessageId = await RecordAsync(host, first.Value, null, "where are the strap cars?", userId: 55, messageId);

        var followUp = await host.SendAsync(Ask(NewSnowflake(), botMessageId, "can you show [2] bigger?"));

        var prompt = chat.ReceivedCalls().Select(call => call.GetArguments()[0]).OfType<AiChatRequest>().Last()
            .Messages[^1].ToPlainText();
        prompt.Should().Contain("[3] (picture, usually with a distribution map) Mongolia > Car metas (the user saw this as 2)");
        followUp.Value.Images.Should().ContainSingle().Which.ImageUrl.Should().Be("https://relay/map.png");
    }

    [Fact]
    public async Task AskAi_CreditsWhatAFollowUpFound_NotWhatThePreviousAnswerCited_WhenItCitesNothing()
    {
        // The carried excerpts lead a follow-up's list. Credited as its closest matches, an uncited
        // answer about Indonesia listed the Malaysia guides the answer before it had cited.
        using var host = CreateHost();
        ArrangeProviders(host, answer: "unused");
        var index = host.Mock<IKnowledgeIndex>();

        var malaysia = Hit("Malaysian bollards are white with a red band.", "https://www.plonkit.net/malaysia#b1");
        var indonesia = Hit("Indonesian bollards are black and white.", "https://www.plonkit.net/indonesia#b1");
        var indonesiaToo = Hit("Indonesian kilometre markers are yellow.", "https://www.plonkit.net/indonesia#k2");
        index.SearchAsync(Arg.Any<KnowledgeQuery>(), Arg.Any<CancellationToken>()).Returns([malaysia], [indonesia, indonesiaToo]);
        index.ReadChunksAsync(Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>()).Returns([malaysia with { Score = 0f }]);
        ArrangeCompletions(host,
            Success("White with a red band [1].", "test/model"),
            Success("Black and white, mostly.", "test/model"));

        var messageId = NewSnowflake();
        var first = await host.SendAsync(Ask(messageId, null, "Malaysian bollards?"));
        var botMessageId = await RecordAsync(host, first.Value, null, "Malaysian bollards?", userId: 55, messageId);

        var followUp = await host.SendAsync(Ask(NewSnowflake(), botMessageId, "what about Indonesia?"));

        followUp.Value.Sources.Should().OnlyContain(source => source.Marker == null);
        followUp.Value.Sources.Select(source => source.Url).Should().Equal(
            ["https://www.plonkit.net/indonesia#b1", "https://www.plonkit.net/indonesia#k2"],
            "the closest matches are what was found for this question, not the guides the previous answer cited");
        followUp.Value.RetrievedSourceUrls.Should().StartWith("https://www.plonkit.net/malaysia#b1",
            "the carried excerpt was still offered first");
    }

    [Fact]
    public async Task AskAi_CreditsTheCarriedGuides_WhenAnUncitedFollowUpFoundNothingElse()
    {
        using var host = CreateHost();
        ArrangeProviders(host, answer: "unused");
        var index = host.Mock<IKnowledgeIndex>();

        var malaysia = Hit("Malaysian bollards are white with a red band.", "https://www.plonkit.net/malaysia#b1");
        index.SearchAsync(Arg.Any<KnowledgeQuery>(), Arg.Any<CancellationToken>()).Returns([malaysia], []);
        index.ReadChunksAsync(Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>()).Returns([malaysia with { Score = 0f }]);
        ArrangeCompletions(host,
            Success("White with a red band [1].", "test/model"),
            Success("As before, white with a red band.", "test/model"));

        var messageId = NewSnowflake();
        var first = await host.SendAsync(Ask(messageId, null, "Malaysian bollards?"));
        var botMessageId = await RecordAsync(host, first.Value, null, "Malaysian bollards?", userId: 55, messageId);

        var followUp = await host.SendAsync(Ask(NewSnowflake(), botMessageId, "are you sure?"));

        followUp.Value.Sources.Select(source => source.Url).Should().Equal("https://www.plonkit.net/malaysia#b1");
    }

    [Fact]
    public async Task RecordAiTurns_StoresTheToolCallsAnAnswerMade()
    {
        using var host = CreateHost();
        ArrangeProviders(host, answer: "unused");
        host.Mock<IKnowledgeIndex>().SearchAsync(Arg.Any<KnowledgeQuery>(), Arg.Any<CancellationToken>())
            .Returns([Hit("Snow poles.", "https://www.plonkit.net/finland#7DPm")]);
        ArrangeCompletions(host,
            ToolCalls(("call-1", AnswerTools.SearchGuides, """{"query": "snow poles"}""")),
            Success("Finnish snow poles [1].", "test/model"));

        var messageId = NewSnowflake();
        var answer = await host.SendAsync(Ask(messageId, null, "snow bollards in the Nordics?"));
        var botMessageId = await RecordAsync(host, answer.Value, null, "snow bollards in the Nordics?", userId: 55, messageId);

        await using var db = fixture.CreateDbContext();
        var stored = await db.AiConversationTurns.AsNoTracking().SingleAsync(turn => turn.DiscordMessageId == botMessageId);
        AiToolCallLog.Deserialize(stored.ToolCallsJson).Should().ContainSingle().Which.Should().BeEquivalentTo(
            new { Round = 1, Tool = AnswerTools.SearchGuides, Outcome = AiToolCallOutcome.NothingNew });
    }

    [Fact]
    public async Task AskAi_BoostsTheCountryTheQuestionNames_UnderEverySpellingTheIndexUses()
    {
        using var host = CreateHost();
        ArrangeProviders(host, answer: "answer");
        host.Mock<IKnowledgeIndex>().ListCountriesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<string>>(["usa", "united states", "brazil"]));

        await host.SendAsync(Ask(NewSnowflake(), null, "which states in the US don't require front plates?"));

        var query = LastQuery(host);
        query.BoostCountries.Should().BeEquivalentTo(["usa", "united states"]);
        query.Country.Should().BeNull("the answer path boosts; only /ai search filters");
    }

    [Fact]
    public async Task AskAi_PlansTheSearch_ForOneMoreRequest()
    {
        using var host = CreateHost(planner: true);
        ArrangeProviders(host, answer: "Pole supports are absent in Ceará.");
        var chat = ArrangePlanner(host, Success(
            """{"question": "Which Brazilian states have poles without supports?", "searches": ["Brazil poles no supports"], "countries": ["Brazil"], "phrases": ["no supports"]}""",
            "test/model"));
        host.Mock<IKnowledgeIndex>().ListCountriesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<string>>(["brazil"]));

        var before = await ReadTodaysBudgetAsync();
        var result = await host.SendAsync(Ask(NewSnowflake(), null, "which states in Brazil don't do pole supports?"));
        var after = await ReadTodaysBudgetAsync();

        result.Value.Text.Should().Be("Pole supports are absent in Ceará.");
        (after.Requests - before.Requests).Should().Be(3, "the embedding, the planner and the answer");
        await chat.Received(2).CompleteAsync(Arg.Any<AiChatRequest>(), Arg.Any<CancellationToken>());

        EmbeddedTexts(host).Should().Equal(
            "Which Brazilian states have poles without supports?",
            "which states in Brazil don't do pole supports?",
            "Brazil poles no supports");

        var query = LastQuery(host);
        query.ExtraTextVectors.Should().HaveCount(2);
        query.BoostCountries.Should().Equal("brazil");
        query.KeyPhrases.Should().Equal("no supports");

        // Text only, at a low temperature, with its own instruction and the question.
        var planned = PlannerRequests(chat).Single();
        planned.Temperature.Should().BeLessThan(0.2);
        planned.Messages[^1].ToPlainText().Should().Contain("which states in Brazil don't do pole supports?");
    }

    [Fact]
    public async Task AskAi_AnswersWithoutAPlan_WhenTheBudgetCannotCoverThePlanner()
    {
        await ResetTodaysBudgetAsync();

        // Two requests cover the question; the planner is what is given up, not the question.
        using var host = CreateHost(dailyBudget: 2, planner: true);
        ArrangeProviders(host, answer: "answer");
        var chat = ArrangePlanner(host, Success("""{"question": "never asked"}""", "test/model"));

        var result = await host.SendAsync(Ask(NewSnowflake(), null, "first"));

        result.IsSuccess.Should().BeTrue();
        PlannerRequests(chat).Should().BeEmpty();
        (await host.SendAsync(Ask(NewSnowflake(), null, "second"))).Error.Code.Should().Be("ai.budget_exhausted");
    }

    [Fact]
    public async Task AskAi_StopsPlanning_OnceTheDaysRequestsReachThePlannersShare()
    {
        await ResetTodaysBudgetAsync();

        // Half of ten: the first question is planned (2 + 1 = 3), the second would take the day to 6.
        using var host = CreateHost(dailyBudget: 10, planner: true, plannerSharePercent: 50);
        ArrangeProviders(host, answer: "answer");
        var chat = ArrangePlanner(host, Success("""{"question": "Where are Khasi pines found?"}""", "test/model"));

        (await host.SendAsync(Ask(NewSnowflake(), null, "where can I find Khasi pines?"))).IsSuccess.Should().BeTrue();
        (await host.SendAsync(Ask(NewSnowflake(), null, "and chir pines?"))).IsSuccess.Should().BeTrue();

        PlannerRequests(chat).Should().ContainSingle();
        (await ReadTodaysBudgetAsync()).Requests.Should().Be(5, "two questions and one plan");
    }

    [Theory]
    [InlineData("I'm sorry, I can't help with that.")]
    [InlineData("User Safety: safe")]
    public async Task AskAi_SearchesByTheQuestion_WhenThePlannersReplyCannotBeRead(string reply)
    {
        using var host = CreateHost(planner: true);
        ArrangeProviders(host, answer: "answer");
        ArrangePlanner(host, Success(reply, "test/planner-model"));

        var result = await host.SendAsync(Ask(NewSnowflake(), null, "where can I find Khasi pines?"));

        result.IsSuccess.Should().BeTrue();
        LastQuery(host).ExtraTextVectors.Should().BeEmpty();
        EmbeddedTexts(host).Should().Equal("where can I find Khasi pines?");
    }

    [Fact]
    public async Task AskAi_KeepsAClassifierThatAnsweredThePlanner_OutOfTheAnswersChain()
    {
        using var host = CreateHost(planner: true);
        ArrangeProviders(host, answer: "answer");
        ArrangePlanner(host, Success("User Safety: safe", "test/classifier"));

        await host.SendAsync(Ask(NewSnowflake(), null, "where can I find Khasi pines?"));

        host.Mock<IChatModelCatalog>().Received(1).ReportUnfit("test/classifier");
    }

    [Fact]
    public async Task AskAi_StillAnswers_WhenThePlannerFails_AndBlamesItsModels()
    {
        using var host = CreateHost(planner: true);
        ArrangeProviders(host, answer: "answer");
        ArrangePlanner(host, Failure(ChatErrorCodes.RequestFailed));

        var result = await host.SendAsync(Ask(NewSnowflake(), null, "bollards?"));

        result.IsSuccess.Should().BeTrue("planning improves a question; it is never a reason for it to fail");
        result.Value.Text.Should().Be("answer");
        host.Mock<IChatModelCatalog>().Received().ReportChainOutcome(Arg.Any<IReadOnlyList<string>>(), null);
    }

    [Fact]
    public async Task AskAi_StillAnswers_WhenThePlannerThrows()
    {
        using var host = CreateHost(planner: true);
        ArrangeProviders(host, answer: "answer");
        host.Mock<IChatModelClient>()
            .CompleteAsync(Arg.Is<AiChatRequest>(request => IsPlanner(request)), Arg.Any<CancellationToken>())
            .Returns<Task<Result<AiChatResponse>>>(_ => throw new InvalidOperationException("circuit open"));

        var result = await host.SendAsync(Ask(NewSnowflake(), null, "bollards?"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Text.Should().Be("answer");
    }

    [Fact]
    public async Task AskAi_StopsWaitingForASlowPlanner_WithoutBlamingItsModels()
    {
        using var host = CreateHost(planner: true, plannerTimeoutSeconds: 1);
        ArrangeProviders(host, answer: "answer");
        host.Mock<IChatModelClient>()
            .CompleteAsync(Arg.Is<AiChatRequest>(request => IsPlanner(request)), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                await Task.Delay(Timeout.Infinite, call.Arg<CancellationToken>());
                return Success("never", "test/model");
            });

        var waited = System.Diagnostics.Stopwatch.StartNew();
        var result = await host.SendAsync(Ask(NewSnowflake(), null, "bollards?"));

        result.Value.Text.Should().Be("answer");
        waited.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(15));
        host.Mock<IChatModelCatalog>().DidNotReceive().ReportChainOutcome(Arg.Any<IReadOnlyList<string>>(), null);
    }

    [Fact]
    public async Task AskAi_SkipsThePlannerForAWhile_AfterItTimedOut()
    {
        // Behind a slow model every question would wait the planner's whole timeout for nothing:
        // dots-3-note-preview planned within 20 s for only 11 of 49 replayed questions.
        var clock = new FixedTimeProvider(DateTimeOffset.UtcNow);
        using var host = CreateHost(planner: true, plannerTimeoutSeconds: 1, clock: clock);
        ArrangeProviders(host, answer: "answer");
        var chat = host.Mock<IChatModelClient>();
        chat.CompleteAsync(Arg.Is<AiChatRequest>(request => IsPlanner(request)), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                await Task.Delay(Timeout.Infinite, call.Arg<CancellationToken>());
                return Success("never", "test/model");
            });

        (await host.SendAsync(Ask(NewSnowflake(), null, "bollards?"))).IsSuccess.Should().BeTrue();

        var before = await ReadTodaysBudgetAsync();
        var waited = System.Diagnostics.Stopwatch.StartNew();
        (await host.SendAsync(Ask(NewSnowflake(), null, "and road lines?"))).IsSuccess.Should().BeTrue();
        var after = await ReadTodaysBudgetAsync();

        PlannerRequests(chat).Should().ContainSingle("the planner sits out its pause");
        (after.Requests - before.Requests).Should().Be(2, "a skipped planner costs nothing");
        waited.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1), "nothing waited for the planner");

        clock.Now += TimeSpan.FromMinutes(31);
        (await host.SendAsync(Ask(NewSnowflake(), null, "and poles?"))).IsSuccess.Should().BeTrue();

        PlannerRequests(chat).Should().HaveCount(2, "after the pause the planner is tried again");
    }

    [Fact]
    public async Task AskAi_OffersTools_YetAQuestionAnsweredStraightAwayCostsTwoRequests()
    {
        using var host = CreateHost();
        var chat = ArrangeProviders(host, answer: "Ghanaian bollards are white.");

        var before = await ReadTodaysBudgetAsync();
        (await host.SendAsync(Ask(NewSnowflake(), null, "bollards in Ghana?"))).IsSuccess.Should().BeTrue();
        var after = await ReadTodaysBudgetAsync();

        var request = chat.ReceivedCalls().Select(call => call.GetArguments()[0]).OfType<AiChatRequest>().Single();
        request.Tools.Should().NotBeNullOrEmpty();
        request.ModelChain.Should().NotContain("openrouter/free", "the router is never asked with tools");
        (after.Requests - before.Requests).Should().Be(2, "the embedding and the one call, as before tools");
    }

    [Fact]
    public async Task AskAi_ClaimsEachToolRound_AndCitesWhatItFound()
    {
        using var host = CreateHost();
        ArrangeProviders(host, answer: "unused");
        host.Mock<IKnowledgeIndex>().SearchAsync(Arg.Any<KnowledgeQuery>(), Arg.Any<CancellationToken>())
            .Returns(
                [Hit("Roads starting with MR…", "https://www.plonkit.net/eswatini#m1jr")],
                [Hit("MR roads have white signs.", "https://www.plonkit.net/eswatini#k2")]);
        ArrangeCompletions(host,
            ToolCalls(("call-1", AnswerTools.SearchGuides, """{"query": "MR road signs", "country": "Eswatini"}""")),
            Success("MR roads have white signs [2].", "test/model"));

        var before = await ReadTodaysBudgetAsync();
        var result = await host.SendAsync(Ask(NewSnowflake(), null, "what do MR road signs look like?"));
        var after = await ReadTodaysBudgetAsync();

        result.IsSuccess.Should().BeTrue();
        (after.Requests - before.Requests).Should().Be(4, "the question's two, the round's call and its search's embedding");
        await host.Mock<IEmbedder>().Received(2).EmbedAsync(Arg.Any<IReadOnlyList<EmbeddingInput>>(), Arg.Any<CancellationToken>());

        result.Value.Text.Should().Be("MR roads have white signs [1].");
        result.Value.Sources.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new { Marker = 1, Url = "https://www.plonkit.net/eswatini#k2" }, "a tool's excerpt is cited like any other");
        result.Value.RetrievedSourceUrls.Should().Equal(
            ["https://www.plonkit.net/eswatini#m1jr", "https://www.plonkit.net/eswatini#k2"],
            "the archive lists what the tools offered after what was retrieved");
        result.Value.CitedSourceUrls.Should().Equal("https://www.plonkit.net/eswatini#k2");
        result.Value.ToolCalls.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new { Round = 1, Tool = AnswerTools.SearchGuides, Outcome = AiToolCallOutcome.Answered, NewExcerpts = 1 });
    }

    [Fact]
    public async Task AskAi_HasTheModelAnswer_OnceTheAllowanceCannotPayForAnotherRound()
    {
        await ResetTodaysBudgetAsync();

        // Two for the question, two for one round: nothing is left for a second.
        using var host = CreateHost(dailyBudget: 4);
        ArrangeProviders(host, answer: "unused");
        var chat = ArrangeCompletions(host,
            ToolCalls(("call-1", AnswerTools.SearchGuides, """{"query": "poles"}""")),
            Success("From what I found.", "test/model"));

        var result = await host.SendAsync(Ask(NewSnowflake(), null, "poles?"));

        result.Value.Text.Should().Be("From what I found.");
        var last = chat.ReceivedCalls().Select(call => call.GetArguments()[0]).OfType<AiChatRequest>().Last();
        last.ToolChoice.Should().Be(AiToolChoice.None);
        (await ReadTodaysBudgetAsync()).Requests.Should().Be(4);
    }

    [Fact]
    public async Task AskAi_RefusesCleanly_WhenAnotherQuestionTakesTheLastRequestsMidAnswer()
    {
        await ResetTodaysBudgetAsync();

        using var host = CreateHost(dailyBudget: 4);
        ArrangeProviders(host, answer: "unused");
        host.Mock<IChatModelClient>().CompleteAsync(Arg.Any<AiChatRequest>(), Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                // Another question claims the rest of the day's allowance while this one is thinking.
                await using var db = fixture.CreateDbContext();
                await new Infrastructure.OutputAdapters.Repositories.EfAiBudgetRepository(db)
                    .TryReserveRequestsAsync(DateOnly.FromDateTime(DateTime.UtcNow), 2, 4);
                return ToolCalls(("call-1", AnswerTools.SearchGuides, """{"query": "poles"}"""));
            });

        var result = await host.SendAsync(Ask(NewSnowflake(), null, "poles?"));

        result.Error.Code.Should().Be("ai.budget_exhausted", "an answer that cannot be paid for is refused, not left empty");
        (await ReadTodaysBudgetAsync()).Requests.Should().Be(4, "nothing past the allowance was claimed");
    }

    [Fact]
    public async Task AskAi_NeverOffersToolsThroughTheRouter()
    {
        using var host = CreateHost();
        ArrangeProviders(host, answer: "unused");
        ArrangeChains(host, first: ["openrouter/free"], retry: []);
        var chat = ArrangeCompletions(host, Success("An answer.", "some/random-model"));

        (await host.SendAsync(Ask(NewSnowflake(), null, "poles?"))).IsSuccess.Should().BeTrue();

        chat.ReceivedCalls().Select(call => call.GetArguments()[0]).OfType<AiChatRequest>().Single()
            .Tools.Should().BeNull("the router picks a model at random, which may not call tools");
    }

    private async Task ResetTodaysBudgetAsync()
    {
        await using var db = fixture.CreateDbContext();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""DELETE FROM "AiDailyBudgets" WHERE "DateUtc" = {DateOnly.FromDateTime(DateTime.UtcNow)}""");
    }

    /// <summary>
    /// Today's row is shared by every test in the collection, which run one at a time, so a test reads
    /// it before and after and compares the difference rather than the totals.
    /// </summary>
    private async Task<(int Requests, long PromptTokens)> ReadTodaysBudgetAsync()
    {
        await using var db = fixture.CreateDbContext();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var row = await db.AiDailyBudgets.AsNoTracking().SingleOrDefaultAsync(budget => budget.DateUtc == today);
        return (row?.RequestCount ?? 0, row?.PromptTokens ?? 0);
    }

    /// <summary>Arranges the chain for a first attempt and, for any request excluding tried models, the retry's.</summary>
    private static IChatModelCatalog ArrangeChains(MediatorTestHost host, string[] first, string[] retry)
    {
        var catalog = host.Mock<IChatModelCatalog>();

        catalog.ReadChainAsync(Arg.Any<ChatModelRequirements>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<string>>(first));
        catalog.ReadChainAsync(
                Arg.Is<ChatModelRequirements>(r => r.ExcludedModelIds != null && r.ExcludedModelIds.Count > 0),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<string>>(retry));

        return catalog;
    }

    /// <summary>Scripts successive completions, one per attempt.</summary>
    private static IChatModelClient ArrangeCompletions(
        MediatorTestHost host,
        Result<AiChatResponse> first,
        params Result<AiChatResponse>[] rest)
    {
        var chat = host.Mock<IChatModelClient>();
        chat.CompleteAsync(Arg.Any<AiChatRequest>(), Arg.Any<CancellationToken>()).Returns(first, rest);
        return chat;
    }

    private static Result<AiChatResponse> Success(string text, string model) =>
        Result<AiChatResponse>.Success(new AiChatResponse(text, model, new AiTokenUsage(10, 5)));

    private static Result<AiChatResponse> Failure(string code) =>
        Result<AiChatResponse>.Failure(Error.Unexpected(code, "failed"));

    private static Result<AiChatResponse> ToolCalls(params (string Id, string Name, string Arguments)[] calls) =>
        Result<AiChatResponse>.Success(new AiChatResponse(
            string.Empty, "test/model", new AiTokenUsage(10, 5),
            [.. calls.Select(call => new AiToolCall(call.Id, call.Name, call.Arguments))], "tool_calls"));

    /// <param name="planner">
    /// Off unless a test is about it: the other tests script the answer's completions one by one, and
    /// the planner's request would take the first of them. The planner's own tests are above.
    /// </param>
    private MediatorTestHost CreateHost(
        int dailyBudget = 1000,
        int perUserPerHour = 0,
        bool planner = false,
        int plannerTimeoutSeconds = 20,
        int plannerSharePercent = 100,
        TimeProvider? clock = null,
        bool tools = true,
        int maxCarried = 6) =>
        new(
            fixture.ConnectionString,
            configure: clock is null ? null : services => services.AddSingleton(clock),
            configurationValues: new Dictionary<string, string?>
            {
                ["AI:Active"] = "true",
                ["AI:OpenRouter:DailyRequestBudget"] = dailyBudget.ToString(),
                ["AI:MaxRequestsPerUserPerHour"] = perUserPerHour.ToString(),
                ["AI:MaxImagesInReply"] = "3",
                ["AI:Retrieval:QueryPlanner:Enabled"] = planner.ToString(),
                ["AI:Retrieval:QueryPlanner:TimeoutSeconds"] = plannerTimeoutSeconds.ToString(),
                ["AI:Retrieval:QueryPlanner:MaxDailyBudgetPercent"] = plannerSharePercent.ToString(),
                ["AI:Tools:Enabled"] = tools.ToString(),
                ["AI:Retrieval:MaxCarriedExcerpts"] = maxCarried.ToString()
            });

    /// <summary>Scripts the planner's reply, leaving the answer's as <see cref="ArrangeProviders"/> set it.</summary>
    private static IChatModelClient ArrangePlanner(MediatorTestHost host, Result<AiChatResponse> reply)
    {
        var chat = host.Mock<IChatModelClient>();
        chat.CompleteAsync(Arg.Is<AiChatRequest>(request => IsPlanner(request)), Arg.Any<CancellationToken>())
            .Returns(reply);
        return chat;
    }

    private static bool IsPlanner(AiChatRequest request) =>
        request.Messages.Count > 0 && request.Messages[0].ToPlainText() == RetrievalQueryPlanner.SystemPrompt;

    private static IEnumerable<AiChatRequest> PlannerRequests(IChatModelClient chat) =>
        chat.ReceivedCalls().Select(call => call.GetArguments()[0]).OfType<AiChatRequest>().Where(IsPlanner);

    private static KnowledgeQuery LastQuery(MediatorTestHost host) =>
        host.Mock<IKnowledgeIndex>().ReceivedCalls().Select(call => call.GetArguments()[0]).OfType<KnowledgeQuery>().Last();

    private static IReadOnlyList<string> EmbeddedTexts(MediatorTestHost host) =>
    [
        .. host.Mock<IEmbedder>().ReceivedCalls()
            .Select(call => call.GetArguments()[0])
            .OfType<IReadOnlyList<EmbeddingInput>>()
            .Last()
            .OfType<TextEmbeddingInput>()
            .Select(input => input.Text)
    ];

    /// <summary>Scripts the faked provider ports so a question completes without touching the network.</summary>
    private static IChatModelClient ArrangeProviders(MediatorTestHost host, string answer)
    {
        host.Mock<IEmbedder>().EmbedAsync(Arg.Any<IReadOnlyList<EmbeddingInput>>(), Arg.Any<CancellationToken>())
            .Returns(call => Result<IReadOnlyList<ReadOnlyMemory<float>>>.Success(
                [.. ((IReadOnlyList<EmbeddingInput>)call[0]).Select(_ => new ReadOnlyMemory<float>(new float[4]))]));

        host.Mock<IKnowledgeIndex>().SearchAsync(Arg.Any<KnowledgeQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<KnowledgeHit>>([]));

        host.Mock<IChatModelCatalog>().ReadChainAsync(Arg.Any<ChatModelRequirements>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<string>>(["test/model", "openrouter/free"]));

        var chat = host.Mock<IChatModelClient>();
        chat.CompleteAsync(Arg.Any<AiChatRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result<AiChatResponse>.Success(new AiChatResponse(answer, "test/model", new AiTokenUsage(10, 5))));

        return chat;
    }

    /// <summary>Asks a question and stores the resulting exchange, as the gateway would.</summary>
    private async Task<(ulong UserMessageId, ulong BotMessageId, ulong ConversationId)> SeedExchangeAsync(
        MediatorTestHost host,
        string question,
        string answerText)
    {
        var chat = host.Mock<IChatModelClient>();
        chat.CompleteAsync(Arg.Any<AiChatRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result<AiChatResponse>.Success(new AiChatResponse(answerText, "test/model", new AiTokenUsage(1, 1))));

        var userMessageId = NewSnowflake();
        var answer = await host.SendAsync(Ask(userMessageId, null, question));
        var botMessageId = await RecordAsync(host, answer.Value, null, question, userId: 55, userMessageId);

        return (userMessageId, botMessageId, answer.Value.ConversationId);
    }

    private static async Task<ulong> RecordAsync(
        MediatorTestHost host,
        AiAnswer answer,
        ulong? parentId,
        string question,
        ulong userId,
        ulong? userMessageId = null,
        IReadOnlyList<string>? images = null)
    {
        var botMessageId = NewSnowflake();

        await host.SendAsync(new RecordAiTurnsCommand(
            userMessageId ?? NewSnowflake(), parentId, botMessageId, answer.ConversationId,
            ChannelId: 5, GuildId: 7, userId, BotUserId: 1,
            question, images ?? [], answer.Text, answer.ModelUsed,
            answer.RetrievedSourceUrls, answer.CitedSourceUrls, EarlierBotMessageIds: [], answer.Depth,
            answer.OfferedChunkIds, answer.CitedChunkIds, answer.ToolCalls));

        return botMessageId;
    }

    private static AskAiCommand Ask(
        ulong messageId,
        ulong? parentId,
        string content,
        ulong? authorId = null,
        IReadOnlyList<string>? images = null) =>
        new(messageId, parentId, ChannelId: 5, GuildId: 7, authorId ?? NewSnowflake(), content, images ?? []);

    private static ulong NewSnowflake() => (ulong)Random.Shared.NextInt64(1_000_000_000, long.MaxValue);

    private static KnowledgeHit Hit(string text, string sourceUrl) =>
        new(Guid.NewGuid(), 0.9f, KnowledgeChunkKind.Text, text, sourceUrl, ImageUrl: null,
            "Plonk It", "eswatini", "Eswatini > Identifying", "Plonk It team", 0);
}
