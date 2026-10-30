using Configuration;
using FluentAssertions;
using GeoClubBot.Tests.TestBuilders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using UseCases.OutputPorts.AI;
using UseCases.OutputPorts.Repositories;
using UseCases.UseCases.AI.Conversations;
using UseCases.UseCases.AI.Retrieval;
using UseCases.UseCases.AI.Tools;
using Utilities;
using Xunit;

namespace GeoClubBot.Tests.AI;

/// <summary>
/// Drives the answer's tool loop with a scripted chat client: what is sent on each call, what each round
/// costs, and that every way a round can go wrong still ends in an answer or a clean refusal. The tools
/// run for real, against substituted index, embedder and species database.
/// </summary>
public sealed class AiAnswerLoopTests
{
    private static readonly DateOnly Today = new(2026, 10, 25);

    [Fact]
    public async Task AnswersInOneCall_WhenTheModelNeedsNoTool()
    {
        var fixture = new Fixture();
        fixture.Chat.Then(Answer("Pole supports are rare in Ceará [1]."));

        var result = await fixture.AskAsync();

        result.IsSuccess.Should().BeTrue();
        var request = fixture.Chat.Requests.Should().ContainSingle().Subject;
        request.Tools.Should().NotBeNull().And.HaveCount(3);
        request.ToolChoice.Should().Be(AiToolChoice.Auto);
        request.Messages[0].ToPlainText().Should().StartWith(AiPromptBuilder.SystemPrompt).And.EndWith(AnswerTools.SystemPrompt);
        fixture.Budget.Spent.Should().Be(Fixture.QuestionCost, "a question answered straight away costs what it always did");
        result.Value.Excerpts.Should().HaveCount(2);
        result.Value.ToolCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task RunsASearch_AndAnswersFromWhatItFound()
    {
        var fixture = new Fixture();
        var found = Hit("Ceará poles have no supports.", "https://www.plonkit.net/brazil#u4vX", localKey: "u4vX");
        fixture.Index.SearchAsync(Arg.Any<KnowledgeQuery>(), Arg.Any<CancellationToken>())
            .Returns([found, fixture.Hits[0]]);
        fixture.Chat
            .Then(ToolCalls(("call-1", AnswerTools.SearchGuides, """{"query": "pole supports", "country": "Brazil"}""")))
            .Then(Answer("Ceará's poles have no supports [3]."));

        var result = await fixture.AskAsync();

        result.IsSuccess.Should().BeTrue();
        fixture.Budget.Spent.Should().Be(Fixture.QuestionCost + 2, "the call that answers, and the round's one embedding");
        await fixture.Embedder.Received(1).EmbedAsync(
            Arg.Is<IReadOnlyList<EmbeddingInput>>(inputs => inputs.Count == 1 && ((TextEmbeddingInput)inputs[0]).Text == "pole supports"),
            Arg.Any<CancellationToken>());

        var query = fixture.Index.ReceivedCalls().Select(call => call.GetArguments()[0]).OfType<KnowledgeQuery>().Single();
        query.BoostCountries.Should().Equal("brazil");
        query.Country.Should().BeNull("a country the model names is preferred, never required");
        query.Limit.Should().Be(2 + AnswerToolbox.MaxSearchExcerpts);

        var second = fixture.Chat.Requests[1];
        second.Messages.Should().HaveCount(fixture.Chat.Requests[0].Messages.Count + 2);
        second.Messages[^2].ToolCalls.Should().ContainSingle().Which.Id.Should().Be("call-1");
        var toolResult = second.Messages[^1].Parts.OfType<AiToolResultPart>().Single();
        toolResult.ToolCallId.Should().Be("call-1");
        toolResult.Content.Should().StartWith("[3] Brazil > Poles")
            .And.Contain("(source: https://www.plonkit.net/brazil#u4vX)")
            .And.NotContain("[4]", "what was offered already is not offered again");

        result.Value.Excerpts.Select(excerpt => excerpt.SourceUrl).Should().HaveCount(3)
            .And.EndWith("https://www.plonkit.net/brazil#u4vX");
        result.Value.ToolCalls.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            Round = 1,
            Tool = AnswerTools.SearchGuides,
            Outcome = AiToolCallOutcome.Answered,
            NewExcerpts = 1
        });
    }

    [Fact]
    public async Task RunsARoundsSearchesTogether_InOneEmbeddingRequest()
    {
        var fixture = new Fixture();
        fixture.Index.SearchAsync(Arg.Any<KnowledgeQuery>(), Arg.Any<CancellationToken>())
            .Returns(
                [Hit("Chilean bollards carry a red band.", "https://www.plonkit.net/chile#c1", sourceKey: "chile")],
                [Hit("Argentine bollards are rare.", "https://www.plonkit.net/argentina#a1", sourceKey: "argentina")]);
        fixture.Chat
            .Then(ToolCalls(
                ("call-chile", AnswerTools.SearchGuides, """{"query": "bollards", "country": "Chile"}"""),
                ("call-argentina", AnswerTools.SearchGuides, """{"query": "bollards", "country": "Argentina"}""")))
            .Then(Answer("Red bands in Chile [3], rare in Argentina [4]."));

        var result = await fixture.AskAsync();

        result.IsSuccess.Should().BeTrue();
        await fixture.Embedder.Received(1).EmbedAsync(
            Arg.Is<IReadOnlyList<EmbeddingInput>>(inputs => inputs.Count == 2), Arg.Any<CancellationToken>());
        fixture.Budget.Spent.Should().Be(Fixture.QuestionCost + 2, "two searches in one round cost one embedding");

        var results = fixture.Chat.Requests[1].Messages.TakeLast(2).Select(message => message.Parts.OfType<AiToolResultPart>().Single()).ToList();
        results.Select(part => part.ToolCallId).Should().Equal("call-chile", "call-argentina");
        results[0].Content.Should().StartWith("[3]").And.Contain("chile#c1");
        results[1].Content.Should().StartWith("[4]").And.Contain("argentina#a1");
    }

    [Fact]
    public async Task ReadsMoreOfTheGuide_AnExcerptCameFrom()
    {
        var fixture = new Fixture();
        fixture.Index.ReadSourceChunksAsync("plonkit", "brazil", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(
            [
                fixture.Hits[0],
                Hit("Paraná poles carry a support on every second pole.", "https://www.plonkit.net/brazil#k2", localKey: "k2"),
                Hit("Rondônia has none.", "https://www.plonkit.net/brazil#k3", localKey: "k3")
            ]);
        fixture.Chat
            .Then(ToolCalls(("call-1", AnswerTools.ReadGuide, """{"excerpt": 1}""")))
            .Then(Answer("Paraná [3], Rondônia [4]."));

        var result = await fixture.AskAsync();

        result.IsSuccess.Should().BeTrue();
        fixture.Budget.Spent.Should().Be(Fixture.QuestionCost + 1, "reading a guide costs no embedding");
        var content = fixture.Chat.Requests[1].Messages[^1].ToPlainText();
        content.Should().Contain("[3] Brazil > Poles").And.Contain("Paraná poles").And.Contain("[4]").And.Contain("Rondônia");
        result.Value.ToolCalls.Single().NewExcerpts.Should().Be(2);
    }

    [Fact]
    public async Task AsksTheSpeciesDatabase_WhereAPlantIsRecorded()
    {
        var fixture = new Fixture();
        fixture.Species.ReadAsync(Arg.Is<SpeciesRangeQuery>(query => query.Name == "khasi pine" && query.CountryCode == "TH"), Arg.Any<CancellationToken>())
            .Returns(Result<SpeciesRange>.Success(new SpeciesRange(
                5285824, "Pinus kesiya Royle ex Gordon", "Pinus kesiya", "SPECIES", MatchedByCommonName: true, FuzzyMatch: false,
                "TH", [new SpeciesRangeArea("Chiang Mai", 40)], 87)));
        fixture.Chat
            .Then(ToolCalls(("call-1", AnswerTools.SpeciesRange, """{"name": "khasi pine", "country": "Thailand"}""")))
            .Then(Answer("Mostly around Chiang Mai [3]."));

        var result = await fixture.AskAsync();

        result.IsSuccess.Should().BeTrue();
        fixture.Budget.Spent.Should().Be(Fixture.QuestionCost + 1, "the species database costs no AI request");
        fixture.Chat.Requests[1].Messages[^1].ToPlainText().Should()
            .StartWith("[3] Where Pinus kesiya is recorded in Thailand (GBIF)")
            .And.Contain("Chiang Mai 40")
            .And.Contain("(source: https://www.gbif.org/species/5285824)");
        result.Value.Excerpts[^1].SourceUrl.Should().Be("https://www.gbif.org/species/5285824");
    }

    [Theory]
    [InlineData(false, "could not be reached", AiToolCallOutcome.Unavailable)]
    [InlineData(true, "GBIF knows no plant called 'glorbleweed'", AiToolCallOutcome.NothingNew)]
    public async Task TellsTheModel_WhenTheSpeciesDatabaseHasNothingToSay(bool notFound, string told, AiToolCallOutcome outcome)
    {
        var fixture = new Fixture();
        if (notFound)
        {
            fixture.Species.ReadAsync(Arg.Any<SpeciesRangeQuery>(), Arg.Any<CancellationToken>())
                .Returns(Result<SpeciesRange>.Failure(Error.NotFound("ai.species_not_found", "none")));
        }

        // Otherwise the substitute's default: a failed result with no error at all, as an unreachable port gives.
        fixture.Chat
            .Then(ToolCalls(("call-1", AnswerTools.SpeciesRange, """{"name": "glorbleweed"}""")))
            .Then(Answer("I could not find where it grows."));

        var result = await fixture.AskAsync();

        result.IsSuccess.Should().BeTrue();
        fixture.Chat.Requests[1].Messages[^1].ToPlainText().Should().Contain(told);
        result.Value.ToolCalls.Single().Outcome.Should().Be(outcome);
        result.Value.Excerpts.Should().HaveCount(2, "nothing was offered");
    }

    [Fact]
    public async Task TellsTheModelWhatWasWrongWithItsArguments_AndCarriesOn()
    {
        var fixture = new Fixture();
        fixture.Chat
            .Then(ToolCalls(
                ("call-1", AnswerTools.SearchGuides, "{not json"),
                ("call-2", AnswerTools.ReadGuide, """{"excerpt": 99}"""),
                ("call-3", "web_search", "{}")))
            .Then(Answer("From the guides [1]."));

        var result = await fixture.AskAsync();

        result.IsSuccess.Should().BeTrue();
        await fixture.Embedder.DidNotReceive().EmbedAsync(Arg.Any<IReadOnlyList<EmbeddingInput>>(), Arg.Any<CancellationToken>());
        fixture.Budget.Spent.Should().Be(Fixture.QuestionCost + 1, "a round whose searches could not be read pays for no embedding");

        var contents = fixture.Chat.Requests[1].Messages.TakeLast(3).Select(message => message.ToPlainText()).ToList();
        contents[0].Should().StartWith("Invalid arguments for search_guides");
        contents[1].Should().Contain("There is no excerpt [99]").And.Contain("[1] to [2]");
        contents[2].Should().Contain("There is no tool called 'web_search'");
        result.Value.ToolCalls.Should().OnlyContain(call => call.Outcome == AiToolCallOutcome.InvalidArguments);
    }

    [Fact]
    public async Task HasTheModelAnswer_OnceItsRoundsAreUsedUp()
    {
        var fixture = new Fixture();
        fixture.Tools.MaxToolRounds = 1;
        fixture.Chat
            .Then(ToolCalls(("call-1", AnswerTools.SearchGuides, """{"query": "poles"}""")))
            .Then(Answer("Done [1]."));

        (await fixture.AskAsync()).IsSuccess.Should().BeTrue();

        var last = fixture.Chat.Requests[1];
        last.ToolChoice.Should().Be(AiToolChoice.None);
        last.Tools.Should().NotBeNullOrEmpty("a conversation holding tool calls keeps its tools declared");

        // Told in words too, after every result: the parameter alone left nemotron-3-ultra writing calls as text.
        last.Messages[^1].Role.Should().Be(AiChatRole.User);
        last.Messages[^1].ToPlainText().Should().Be(AnswerTools.AnswerNow);
        last.Messages[^2].Role.Should().Be(AiChatRole.Tool);
        last.Messages[^3].ToolCalls.Should().ContainSingle().Which.Id.Should().Be("call-1");
    }

    [Fact]
    public async Task HasTheModelAnswer_AfterTwoRounds_ByDefault()
    {
        // No third round contributed a cited excerpt in the 2026-10-29 replay; the second did in five answers.
        var fixture = new Fixture();
        fixture.Chat
            .Then(ToolCalls(("call-1", AnswerTools.SearchGuides, """{"query": "poles"}""")))
            .Then(ToolCalls(("call-2", AnswerTools.SearchGuides, """{"query": "pole supports"}""")))
            .Then(Answer("Done [1]."));

        (await fixture.AskAsync()).IsSuccess.Should().BeTrue();

        fixture.Chat.Requests.Select(request => request.ToolChoice)
            .Should().Equal(AiToolChoice.Auto, AiToolChoice.Auto, AiToolChoice.None);
        fixture.Chat.Requests[1].Messages[^1].Role.Should().Be(AiChatRole.Tool, "a call that may still search is not told to answer");
    }

    /// <summary>What nemotron-3-ultra answered #17 of the 2026-10-29 replay with, told to answer after three rounds.</summary>
    private const string RecordedMarkup =
        "<tool_call>\n<function=search_guides>\n<parameter=country>\nPhilippines\n</parameter>\n<parameter=query>\n"
        + "rice paddy Philippines agriculture\n</parameter>\n</function>\n</tool_call>";

    [Fact]
    public async Task AsksOnceMoreWithoutTools_WhenTheModelWritesItsCallAsText_AfterItsLastRound()
    {
        // #17 of the 2026-10-29 answer replay, as recorded: three rounds of one search each — the third
        // finding only Thai excerpts — then, told to answer, nemotron-3-ultra wrote a fourth search as
        // text, which was posted as the answer under "This answer cites no guide". Without tools the
        // same model answered it.
        var fixture = new Fixture
        {
            Hits =
            [
                Hit("Rice terraces line the Cordillera.", "https://www.plonkit.net/philippines#lS7l", "philippines", "lS7l", "Philippines > Region-specific clues", "philippines"),
                Hit("Northern Luzon has wooden houses.", "https://www.plonkit.net/philippines#9hUv", "philippines", "9hUv", "Philippines > Region-specific clues", "philippines")
            ]
        };
        fixture.Tools.MaxToolRounds = 3;
        fixture.Index.SearchAsync(Arg.Any<KnowledgeQuery>(), Arg.Any<CancellationToken>())
            .Returns(
                [Hit("Grain is dried on the road in Luzon.", "https://www.plonkit.net/philippines#2ctB", "philippines", "2ctB", "Philippines > Region-specific clues", "philippines")],
                [Hit("Central Luzon is flat and green.", "https://www.plonkit.net/philippines#GaHP", "philippines", "GaHP", "Philippines > Region-specific clues", "philippines")],
                [Hit("Thai rice fields are flooded in the wet season.", "https://www.plonkit.net/thailand#ZxIl", "thailand", "ZxIl", "Thailand > Vegetation", "thailand")]);
        fixture.Chat
            .Then(ToolCalls(("call-1", AnswerTools.SearchGuides, """{"country":"Philippines","query":"rice distribution Philippines"}""")))
            .Then(ToolCalls(("call-2", AnswerTools.SearchGuides, """{"country":"Philippines","query":"rice paddies Philippines cultivation"}""")))
            .Then(ToolCalls(("call-3", AnswerTools.SearchGuides, """{"query":"rice cultivation Philippines regions","country":"Philippines"}""")))
            .Then(Answer(RecordedMarkup))
            .Then(Answer("Rice is grown on the Cordillera's terraces [1] and across flat Central Luzon [4]."));

        var result = await fixture.AskAsync("where do they grow rice in the Philippenes?");

        result.IsSuccess.Should().BeTrue();
        result.Value.Response.Text.Should().StartWith("Rice is grown").And.NotContain("<tool_call>");
        result.Value.Excerpts.Should().HaveCount(5, "what the rounds found is still citable");
        result.Value.ToolCalls.Select(call => call.Outcome)
            .Should().Equal(AiToolCallOutcome.Answered, AiToolCallOutcome.Answered, AiToolCallOutcome.OffTarget);

        var told = fixture.Chat.Requests[3];
        told.ToolChoice.Should().Be(AiToolChoice.None);
        told.Messages[^1].ToPlainText().Should().Be(AnswerTools.AnswerNow);
        told.Messages[^2].ToPlainText().Should().StartWith("None of these new excerpts is from Philippines");

        var rescue = fixture.Chat.Requests[4];
        rescue.Tools.Should().BeNull();
        rescue.Messages.Should().NotContain(message => message.Role == AiChatRole.Tool);
        rescue.Messages[^1].ToPlainText().Should().Contain("[4] Philippines > Region-specific clues").And.Contain("Central Luzon is flat");
        rescue.ModelChain.Should().Contain("a/model", "it mis-formatted a call; it is not a model that cannot answer");
        fixture.Catalog.DidNotReceive().ReportUnfit(Arg.Any<string>());
        fixture.Budget.Spent.Should().Be(Fixture.QuestionCost + 3 * 2 + 1, "three rounds of a call and an embedding each, then the last call");
    }

    [Fact]
    public async Task RetriesTheFirstCall_WhenItsAnswerIsACallWrittenAsText_WithoutCallingTheModelUnfit()
    {
        var fixture = new Fixture();
        fixture.Chat
            .Then(Answer(RecordedMarkup, "a/model"))
            .Then(Answer("Pole supports are rare in Ceará [1].", "b/model"));

        var result = await fixture.AskAsync();

        result.Value.Response.ModelUsed.Should().Be("b/model");
        fixture.Catalog.DidNotReceive().ReportUnfit(Arg.Any<string>());
    }

    [Fact]
    public async Task NeverPostsACallWrittenAsText_EvenWhenTheLastCallWritesOneToo()
    {
        var fixture = new Fixture();
        fixture.Tools.MaxToolRounds = 1;
        fixture.Chat
            .Then(ToolCalls(("call-1", AnswerTools.SearchGuides, """{"query": "poles"}""")))
            .Then(Answer(RecordedMarkup))
            .Then(Answer("""{"name": "search_guides", "arguments": {"query": "poles"}}"""));

        var result = await fixture.AskAsync();

        result.Error.Code.Should().Be(ChatErrorCodes.ToolCallMarkup);
        fixture.Chat.Requests.Should().HaveCount(3);
    }

    [Fact]
    public async Task TellsTheModel_WhenASearchFindsNothingFromTheCountryItNames()
    {
        var fixture = new Fixture();
        fixture.Index.SearchAsync(Arg.Any<KnowledgeQuery>(), Arg.Any<CancellationToken>())
            .Returns([Hit("Thai rice fields are flooded.", "https://www.plonkit.net/thailand#ZxIl", "thailand", "ZxIl", "Thailand > Vegetation", "thailand")]);
        fixture.Chat
            .Then(ToolCalls(("call-1", AnswerTools.SearchGuides, """{"query": "rice paddies", "country": "Philippines"}""")))
            .Then(Answer("The guides don't say [1]."));

        var result = await fixture.AskAsync("where do they grow rice?");

        var content = fixture.Chat.Requests[1].Messages[^1].ToPlainText();
        content.Should().StartWith("None of these new excerpts is from Philippines. If it is Philippines you need, the guides seem to "
                                   + "have nothing more on it than you have: answer with what you have.")
            .And.Contain("[3] Thailand > Vegetation", "still offered, for a look-alike worth ruling out")
            .And.NotContain("don't cover", "a search can name a country to find others like it; that is no verdict on the answer");
        result.Value.ToolCalls.Single().Should().BeEquivalentTo(new { Outcome = AiToolCallOutcome.OffTarget, NewExcerpts = 1 });
    }

    [Theory]
    [InlineData("Which other countries have flooded rice fields like the Philippines?")]
    [InlineData("where do they grow rice in the Philippenes?")]
    public async Task NeverJudgesASearchNamingNoCountry_ByTheQuestionsCountry(string question)
    {
        // A search giving no country looks across countries on purpose: the six of the 2026-10-29 replay
        // that gave none were each after several. For a comparison these excerpts are the answer.
        var fixture = new Fixture();
        fixture.Index.SearchAsync(Arg.Any<KnowledgeQuery>(), Arg.Any<CancellationToken>())
            .Returns([Hit("Thai rice fields are flooded.", "https://www.plonkit.net/thailand#ZxIl", "thailand", "ZxIl", "Thailand > Vegetation", "thailand")]);
        fixture.Chat
            .Then(ToolCalls(("call-1", AnswerTools.SearchGuides, """{"query": "flooded rice fields"}""")))
            .Then(Answer("Thailand [3]."));

        var result = await fixture.AskAsync(question);

        fixture.Chat.Requests[1].Messages[^1].ToPlainText().Should().StartWith("[3] Thailand > Vegetation");
        result.Value.ToolCalls.Single().Outcome.Should().Be(AiToolCallOutcome.Answered);
    }

    [Theory]
    [InlineData("""{"query": "rice paddies", "country": "Philippines"}""")]
    [InlineData("""{"query": "rice paddies Philippines"}""")]
    public async Task SaysNothingOfACountry_WhenANewExcerptIsFromAGuideFiledUnderNone(string arguments)
    {
        // A library guide with a blank country is filed under none, and may cover several, the one
        // searched among them.
        var fixture = new Fixture();
        fixture.Index.SearchAsync(Arg.Any<KnowledgeQuery>(), Arg.Any<CancellationToken>())
            .Returns(
            [
                Hit("Thai rice fields are flooded.", "https://www.plonkit.net/thailand#ZxIl", "thailand", "ZxIl", "Thailand > Vegetation", "thailand"),
                Hit("Terraced rice across Southeast Asia.", "https://docs.google.com/document/d/sea", "sea", "p4", "Southeast Asian Crops > Rice")
                    with { Country = null }
            ]);
        fixture.Chat
            .Then(ToolCalls(("call-1", AnswerTools.SearchGuides, arguments)))
            .Then(Answer("Terraces [4]."));

        var result = await fixture.AskAsync("where do they grow rice in the Philippines?");

        fixture.Chat.Requests[1].Messages[^1].ToPlainText().Should().StartWith("[3] Thailand > Vegetation");
        result.Value.ToolCalls.Single().Should().BeEquivalentTo(new { Outcome = AiToolCallOutcome.Answered, NewExcerpts = 2 });
    }

    [Theory]
    [InlineData("Is rice grown in Thailand or the Philippines?")]
    [InlineData("where do they grow rice?")]
    public async Task SaysNothingOfACountry_WhenNeitherTheSearchNorTheQuestionIsAboutOne(string question)
    {
        var fixture = new Fixture();
        fixture.Index.SearchAsync(Arg.Any<KnowledgeQuery>(), Arg.Any<CancellationToken>())
            .Returns([Hit("Malaysian rice fields are flooded.", "https://www.plonkit.net/malaysia#r1", "malaysia", "r1", "Malaysia > Vegetation", "malaysia")]);
        fixture.Chat
            .Then(ToolCalls(("call-1", AnswerTools.SearchGuides, """{"query": "rice terraces"}""")))
            .Then(Answer("Malaysia [3]."));

        var result = await fixture.AskAsync(question);

        fixture.Chat.Requests[1].Messages[^1].ToPlainText().Should().StartWith("[3] Malaysia > Vegetation");
        result.Value.ToolCalls.Single().Outcome.Should().Be(AiToolCallOutcome.Answered);
    }

    [Fact]
    public async Task TellsTheModelToStopLooking_WhenASearchFindsNothingNew()
    {
        var fixture = new Fixture();
        fixture.Index.SearchAsync(Arg.Any<KnowledgeQuery>(), Arg.Any<CancellationToken>()).Returns([fixture.Hits[0]]);
        fixture.Chat
            .Then(ToolCalls(("call-1", AnswerTools.SearchGuides, """{"query": "pole supports", "country": "Brazil"}""")))
            .Then(Answer("In the south [1]."));

        var result = await fixture.AskAsync();

        fixture.Chat.Requests[1].Messages[^1].ToPlainText().Should()
            .StartWith("Nothing new").And.Contain("The guides seem not to cover more of this than you have.");
        result.Value.ToolCalls.Single().Outcome.Should().Be(AiToolCallOutcome.NothingNew);
    }

    [Fact]
    public async Task HasTheModelAnswer_OnceTheDeadlineHasPassed()
    {
        var fixture = new Fixture();
        fixture.Chat
            .Then(_ =>
            {
                fixture.Clock.Now += TimeSpan.FromSeconds(fixture.Tools.AnswerDeadlineSeconds + 1);
                return ToolCalls(("call-1", AnswerTools.SearchGuides, """{"query": "poles"}"""));
            })
            .Then(Answer("Done [1]."));

        (await fixture.AskAsync()).IsSuccess.Should().BeTrue();

        fixture.Chat.Requests[1].ToolChoice.Should().Be(AiToolChoice.None);
    }

    [Fact]
    public async Task StillAnswers_WhenALaterCallFails_WithEverythingGathered()
    {
        var fixture = new Fixture();
        fixture.Index.SearchAsync(Arg.Any<KnowledgeQuery>(), Arg.Any<CancellationToken>())
            .Returns([Hit("Ceará poles have no supports.", "https://www.plonkit.net/brazil#u4vX", localKey: "u4vX")]);
        fixture.Chat
            .Then(ToolCalls(("call-1", AnswerTools.SearchGuides, """{"query": "pole supports"}""")))
            .Then(Failure(ChatErrorCodes.RequestFailed))
            .Then(Answer("Ceará [3].", "d/model"));

        var result = await fixture.AskAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.Response.Text.Should().Be("Ceará [3].");
        result.Value.Excerpts.Should().HaveCount(3, "the excerpt the search found is still citable");

        var rescue = fixture.Chat.Requests[2];
        rescue.Tools.Should().BeNull();
        rescue.Messages.Should().NotContain(message => message.Role == AiChatRole.Tool);
        rescue.Messages[^1].ToPlainText().Should().Contain("[3] Brazil > Poles").And.Contain("Ceará poles have no supports.");
        rescue.ModelChain.Should().NotContain(new[] { "a/model", "b/model", "c/model" }, "the chain that failed is not asked again");
        fixture.Budget.Spent.Should().Be(Fixture.QuestionCost + 3, "the failed call, the embedding and the last call");
        fixture.Catalog.Received().ReportChainOutcome(Arg.Is<IReadOnlyList<string>>(chain => chain.Contains("a/model")), null);
    }

    [Fact]
    public async Task TellsTheModelWhatThePreviousAnswerCited_InTheFirstCallAndTheLast()
    {
        // The rescue rebuilds the prompt from everything gathered; the carried excerpts lead it there too.
        var fixture = new Fixture { Carried = 1 };
        fixture.Chat
            .Then(ToolCalls(("call-1", AnswerTools.SearchGuides, """{"query": "pole supports"}""")))
            .Then(Failure(ChatErrorCodes.RequestFailed))
            .Then(Answer("As before [1].", "d/model"));

        (await fixture.AskAsync("show me that picture")).IsSuccess.Should().BeTrue();

        const string note = "([1] is the excerpt your previous answer cited; the rest were found for this question.)";
        fixture.Chat.Requests[0].Messages[^1].ToPlainText().Should().Contain(note);
        fixture.Chat.Requests[2].Messages[^1].ToPlainText().Should().Contain(note);
    }

    [Fact]
    public async Task GivesUp_WhenALaterCallCannotReachTheProvider()
    {
        var fixture = new Fixture();
        fixture.Chat
            .Then(ToolCalls(("call-1", AnswerTools.SearchGuides, """{"query": "poles"}""")))
            .Then(Failure(ChatErrorCodes.Unreachable));

        var result = await fixture.AskAsync();

        result.Error.Code.Should().Be(ChatErrorCodes.Unreachable);
        fixture.Chat.Requests.Should().HaveCount(2, "a provider out of reach would fail the last call the same way");
    }

    [Fact]
    public async Task AsksOnceMoreWithoutTools_WhenTheModelAsksForToolsAfterBeingToldToAnswer()
    {
        var fixture = new Fixture();
        fixture.Tools.MaxToolRounds = 1;
        fixture.Chat
            .Then(ToolCalls(("call-1", AnswerTools.SearchGuides, """{"query": "poles"}""")))
            .Then(ToolCalls(("call-2", AnswerTools.SearchGuides, """{"query": "more poles"}""")))
            .Then(Answer("Done [1]."));

        var result = await fixture.AskAsync();

        result.IsSuccess.Should().BeTrue();
        fixture.Chat.Requests.Should().HaveCount(3);
        fixture.Chat.Requests[2].Tools.Should().BeNull();
    }

    [Fact]
    public async Task NeverPostsAnEmptyAnswer()
    {
        var fixture = new Fixture();
        fixture.Chat
            .Then(ToolCalls(("call-1", AnswerTools.SearchGuides, """{"query": "poles"}""")))
            .Then(Answer("  \n "))
            .Then(Answer("A real answer [1].", "d/model"));

        var result = await fixture.AskAsync();

        result.Value.Response.Text.Should().Be("A real answer [1].");
        fixture.Chat.Requests[2].Tools.Should().BeNull();
    }

    [Fact]
    public async Task NeverOffersToolsThroughTheRouter()
    {
        var fixture = new Fixture { Models = ["openrouter/free"] };
        fixture.Chat.Then(Answer("An answer.", "some/random-model"));

        (await fixture.AskAsync()).IsSuccess.Should().BeTrue();

        var request = fixture.Chat.Requests.Single();
        request.Tools.Should().BeNull("the router picks a model at random, which may not call tools");
        request.Messages[0].ToPlainText().Should().Be(AiPromptBuilder.SystemPrompt);
        request.ModelChain.Should().Equal("openrouter/free");
    }

    [Fact]
    public async Task AnswersInOneCall_WhenToolsAreOff()
    {
        var fixture = new Fixture();
        fixture.Tools.Enabled = false;
        fixture.Chat.Then(Answer("An answer."));

        (await fixture.AskAsync()).IsSuccess.Should().BeTrue();

        fixture.Chat.Requests.Single().Tools.Should().BeNull();
    }

    [Fact]
    public async Task OffersNoTools_WhenTheAllowanceCannotPayForARound()
    {
        var fixture = new Fixture { Cap = Fixture.QuestionCost + 1 };
        fixture.Chat.Then(Answer("An answer."));

        (await fixture.AskAsync()).IsSuccess.Should().BeTrue();

        fixture.Chat.Requests.Single().Tools.Should().BeNull("one request left could not pay for a search and the call that answers from it");
    }

    [Fact]
    public async Task RefusesCleanly_WhenTheAllowanceRunsOutMidAnswer()
    {
        var fixture = new Fixture { Cap = Fixture.QuestionCost + 2 };
        fixture.Chat.Then(_ =>
        {
            // Another question spends the rest of the day's allowance meanwhile.
            fixture.Budget.Spent = fixture.Cap;
            return ToolCalls(("call-1", AnswerTools.SearchGuides, """{"query": "poles"}"""));
        });

        var result = await fixture.AskAsync();

        result.Error.Code.Should().Be(AiAnswerErrors.BudgetExhaustedCode);
        fixture.Chat.Requests.Should().ContainSingle();
        await fixture.Embedder.DidNotReceive().EmbedAsync(Arg.Any<IReadOnlyList<EmbeddingInput>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnswersWithWhatItHas_WhenTheRoundsSearchCannotBePaidFor()
    {
        var fixture = new Fixture { Cap = Fixture.QuestionCost + 2 };
        fixture.Chat
            .Then(_ =>
            {
                // One request is left: enough for the call that answers, not for the search.
                fixture.Budget.Spent = fixture.Cap - 1;
                return ToolCalls(("call-1", AnswerTools.SearchGuides, """{"query": "poles"}"""));
            })
            .Then(Answer("From what I have [1]."));

        var result = await fixture.AskAsync();

        result.IsSuccess.Should().BeTrue();
        await fixture.Embedder.DidNotReceive().EmbedAsync(Arg.Any<IReadOnlyList<EmbeddingInput>>(), Arg.Any<CancellationToken>());
        var last = fixture.Chat.Requests[1];
        last.Messages[^2].ToPlainText().Should().Contain("not available", "the round's result, before the call is told to answer");
        last.ToolChoice.Should().Be(AiToolChoice.None);
        result.Value.ToolCalls.Single().Outcome.Should().Be(AiToolCallOutcome.Unavailable);
    }

    [Fact]
    public async Task AsksAgainWithoutTools_WhenTheProviderRefusesThem()
    {
        var fixture = new Fixture();
        fixture.Chat
            .Then(Failure(ChatErrorCodes.ToolsUnsupported))
            .Then(Answer("An answer [1]."));

        var result = await fixture.AskAsync();

        result.IsSuccess.Should().BeTrue();
        var retry = fixture.Chat.Requests[1];
        retry.Tools.Should().BeNull();
        retry.Messages[0].ToPlainText().Should().Be(AiPromptBuilder.SystemPrompt);
        retry.ModelChain.Should().Contain("a/model", "the models were not at fault");
        fixture.Budget.Spent.Should().Be(Fixture.QuestionCost + 1);
    }

    [Fact]
    public async Task RetriesTheFirstCall_WithToolCapableModelsNotYetTried()
    {
        var fixture = new Fixture();
        fixture.Chat
            .Then(Answer("User Safety: safe", "a/model"))
            .Then(Answer("Red soil is common across Malaysia.", "b/model"));

        var result = await fixture.AskAsync();

        result.Value.Response.ModelUsed.Should().Be("b/model");
        fixture.Chat.Requests[1].Tools.Should().NotBeNull("the retry is asked with tools too");
        fixture.Catalog.Received(1).ReportUnfit("a/model");
        await fixture.Catalog.Received().ReadChainAsync(
            Arg.Is<ChatModelRequirements>(r => r.NeedsTools && r.ExcludedModelIds != null && r.ExcludedModelIds.Contains("a/model")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task KeepsTheModelThatAnswered_AtTheHeadOfTheChain()
    {
        var fixture = new Fixture();
        fixture.Chat
            .Then(ToolCalls("b/model", ("call-1", AnswerTools.SearchGuides, """{"query": "poles"}""")))
            .Then(Answer("Done [1].", "b/model"));

        (await fixture.AskAsync()).IsSuccess.Should().BeTrue();

        fixture.Chat.Requests[1].ModelChain.Should().Equal("b/model", "a/model", "c/model");
    }

    [Fact]
    public async Task NamesACall_TheProviderSentWithoutAnId()
    {
        var fixture = new Fixture();
        fixture.Chat
            .Then(ToolCalls(("", AnswerTools.SearchGuides, """{"query": "poles"}""")))
            .Then(Answer("Done [1]."));

        (await fixture.AskAsync()).IsSuccess.Should().BeTrue();

        var messages = fixture.Chat.Requests[1].Messages;
        messages[^2].ToolCalls.Single().Id.Should().Be("call_1_0");
        messages[^1].Parts.OfType<AiToolResultPart>().Single().ToolCallId.Should().Be("call_1_0");
    }

    [Fact]
    public async Task RunsAtMostTheConfiguredCallsInARound()
    {
        var fixture = new Fixture();
        fixture.Tools.MaxToolCallsPerRound = 1;
        fixture.Chat
            .Then(ToolCalls(
                ("call-1", AnswerTools.SearchGuides, """{"query": "poles"}"""),
                ("call-2", AnswerTools.SearchGuides, """{"query": "bollards"}""")))
            .Then(Answer("Done [1]."));

        var result = await fixture.AskAsync();

        result.Value.ToolCalls.Select(call => call.Outcome).Should().Equal(AiToolCallOutcome.NothingNew, AiToolCallOutcome.NotRun);
        fixture.Chat.Requests[1].Messages[^1].ToPlainText().Should().StartWith("Not run");
        await fixture.Embedder.Received(1).EmbedAsync(
            Arg.Is<IReadOnlyList<EmbeddingInput>>(inputs => inputs.Count == 1), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AsksWithoutTools_WhenTheRetrysProviderRefusesThem_EvenOnTheLastAttempt()
    {
        // The first chain failed for a moment; the retry's provider then refused the tools themselves.
        // The models were not at fault, so the user gets an answer without tools rather than the refusal.
        var fixture = new Fixture();
        fixture.Chat
            .Then(Failure(ChatErrorCodes.RequestFailed))
            .Then(Failure(ChatErrorCodes.ToolsUnsupported))
            .Then(Answer("An answer [1].", "d/model"));

        var result = await fixture.AskAsync();

        result.IsSuccess.Should().BeTrue();
        fixture.Chat.Requests.Should().HaveCount(3);
        fixture.Chat.Requests[1].Tools.Should().NotBeNull();
        var withoutTools = fixture.Chat.Requests[2];
        withoutTools.Tools.Should().BeNull();
        withoutTools.Messages[0].ToPlainText().Should().Be(AiPromptBuilder.SystemPrompt);
        withoutTools.ModelChain.Should().Contain("d/model", "the models that refused the tools were not at fault")
            .And.NotContain(["a/model", "b/model", "c/model"], "the first chain did fail");
        fixture.Budget.Spent.Should().Be(Fixture.QuestionCost + 2);
    }

    [Fact]
    public async Task RetriesWithoutTools_OnceTheDeadlineHasPassed()
    {
        // A slow first attempt used up the answer's time; a retry inviting tool rounds would run the
        // question further past it.
        var fixture = new Fixture();
        fixture.Chat
            .Then(_ =>
            {
                fixture.Clock.Now += TimeSpan.FromSeconds(fixture.Tools.AnswerDeadlineSeconds + 1);
                return Failure(ChatErrorCodes.RequestFailed);
            })
            .Then(Answer("An answer [1].", "d/model"));

        var result = await fixture.AskAsync();

        result.IsSuccess.Should().BeTrue();
        var retry = fixture.Chat.Requests[1];
        retry.Tools.Should().BeNull();
        retry.Messages[0].ToPlainText().Should().Be(AiPromptBuilder.SystemPrompt, "a model told of tools it cannot call tries them in prose");
        retry.ModelChain.Should().NotContain(["a/model", "b/model", "c/model"]);
    }

    [Fact]
    public async Task StopsOfferingExcerpts_OnceTheirNumbersRunOut_AndHasTheModelAnswer()
    {
        // Citations are read up to [99]. An excerpt numbered past it could be cited but never resolved.
        var fixture = new Fixture
        {
            Hits = [.. Enumerable.Range(1, 98).Select(index => Hit($"Retrieved {index}.", $"https://www.plonkit.net/brazil#r{index}"))]
        };
        fixture.Index.SearchAsync(Arg.Any<KnowledgeQuery>(), Arg.Any<CancellationToken>())
            .Returns(
            [
                Hit("Ceará poles have no supports.", "https://www.plonkit.net/brazil#u1"),
                Hit("Piauí poles are round.", "https://www.plonkit.net/brazil#u2"),
                Hit("Bahia poles are square.", "https://www.plonkit.net/brazil#u3")
            ]);
        fixture.Chat
            .Then(ToolCalls(("call-1", AnswerTools.SearchGuides, """{"query": "pole supports"}""")))
            .Then(Answer("Ceará [99]."));

        var result = await fixture.AskAsync();

        result.Value.Excerpts.Should().HaveCount(99);
        result.Value.Excerpts[^1].SourceUrl.Should().Be("https://www.plonkit.net/brazil#u1");
        result.Value.ToolCalls.Single().NewExcerpts.Should().Be(1);

        var next = fixture.Chat.Requests[1];
        next.Messages[^2].ToPlainText().Should().Contain("[99] ").And.Contain("No more excerpts can be offered")
            .And.NotContain("[100]");
        next.ToolChoice.Should().Be(AiToolChoice.None, "another search could offer nothing");
    }

    private static KnowledgeHit Hit(
        string text,
        string sourceUrl,
        string sourceKey = "brazil",
        string localKey = "k1",
        string section = "Brazil > Poles",
        string? country = null) =>
        new(Guid.NewGuid(), 0.5f, KnowledgeChunkKind.Text, text, sourceUrl, null, "Brazil", country ?? sourceKey, section, "Plonk It team", 0,
            "plonkit", sourceKey, localKey);

    private static Result<AiChatResponse> Answer(string text, string model = "a/model") =>
        Result<AiChatResponse>.Success(new AiChatResponse(text, model, new AiTokenUsage(10, 5), FinishReason: "stop"));

    private static Result<AiChatResponse> ToolCalls(params (string Id, string Name, string Arguments)[] calls) =>
        ToolCalls("a/model", calls);

    private static Result<AiChatResponse> ToolCalls(string model, params (string Id, string Name, string Arguments)[] calls) =>
        Result<AiChatResponse>.Success(new AiChatResponse(
            string.Empty, model, new AiTokenUsage(10, 5),
            [.. calls.Select(call => new AiToolCall(call.Id, call.Name, call.Arguments))], "tool_calls"));

    private static Result<AiChatResponse> Failure(string code) =>
        Result<AiChatResponse>.Failure(Error.Unexpected(code, "failed"));

    private sealed class Fixture
    {
        /// <summary>The embedding and the first call, which the handler claims before the loop runs.</summary>
        public const int QuestionCost = 2;

        public ScriptedChatClient Chat { get; } = new();

        public IChatModelCatalog Catalog { get; } = Substitute.For<IChatModelCatalog>();

        public InMemoryBudget Budget { get; } = new();

        public IEmbedder Embedder { get; } = Substitute.For<IEmbedder>();

        public IKnowledgeIndex Index { get; } = Substitute.For<IKnowledgeIndex>();

        public ISpeciesRangeReader Species { get; } = Substitute.For<ISpeciesRangeReader>();

        public AiToolsConfiguration Tools { get; } = new();

        public FixedTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero));

        /// <summary>The roster's tool-capable models, best first; a chain names the first three not excluded.</summary>
        public string[] Models { get; init; } = ["a/model", "b/model", "c/model", "d/model", "e/model"];

        public int Cap { get; init; } = 100;

        /// <summary>How many of <see cref="Hits"/> the previous answer cited, as the handler says.</summary>
        public int Carried { get; init; }

        public IReadOnlyList<KnowledgeHit> Hits { get; init; } =
        [
            Hit("Pole supports are common in the south.", "https://www.plonkit.net/brazil#k1"),
            Hit("Northeastern poles are square.", "https://www.plonkit.net/brazil#k9", localKey: "k9")
        ];

        public Fixture()
        {
            Catalog.ReadChainAsync(Arg.Any<ChatModelRequirements>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    var excluded = ((ChatModelRequirements)call[0]).ExcludedModelIds;
                    return Task.FromResult<IReadOnlyList<string>>(
                        [.. Models.Where(model => excluded?.Contains(model) != true).Take(3)]);
                });

            Embedder.EmbedAsync(Arg.Any<IReadOnlyList<EmbeddingInput>>(), Arg.Any<CancellationToken>())
                .Returns(call => Result<IReadOnlyList<ReadOnlyMemory<float>>>.Success(
                    [.. ((IReadOnlyList<EmbeddingInput>)call[0]).Select(_ => new ReadOnlyMemory<float>(new float[4]))]));

            Index.SearchAsync(Arg.Any<KnowledgeQuery>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IReadOnlyList<KnowledgeHit>>([]));
            Index.ListCountriesAsync(Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IReadOnlyList<string>>(["brazil", "chile", "argentina", "thailand", "malaysia", "philippines"]));
        }

        public Task<Result<AnswerOutcome>> AskAsync(string question = "Where are pole supports rare in Brazil?")
        {
            Budget.Cap = Cap;
            Budget.Spent = Math.Max(Budget.Spent, QuestionCost);

            var retrieval = Options.Create(new AiRetrievalConfiguration());
            var tools = Options.Create(Tools);
            var toolbox = new AnswerToolbox(
                Embedder,
                Index,
                Species,
                new KnowledgeCountryDirectory(Index, retrieval, Clock, NullLogger<KnowledgeCountryDirectory>.Instance),
                retrieval,
                tools,
                NullLogger<AnswerToolbox>.Instance);

            var loop = new AiAnswerLoop(
                Chat,
                Catalog,
                Budget,
                toolbox,
                Options.Create(new AiConfiguration { OpenRouter = new OpenRouterConfiguration { DailyRequestBudget = Cap } }),
                tools,
                Clock,
                NullLogger<AiAnswerLoop>.Instance);

            return loop.AnswerAsync(
                new AnswerRequest(ConversationContext.Empty, question, [], Hits, Today, Clock.GetUtcNow(), Carried),
                CancellationToken.None);
        }
    }

    /// <summary>Answers each call with the next scripted reply, and records what was asked.</summary>
    private sealed class ScriptedChatClient : IChatModelClient
    {
        private readonly Queue<Func<AiChatRequest, Result<AiChatResponse>>> _replies = new();

        public List<AiChatRequest> Requests { get; } = [];

        public ScriptedChatClient Then(Result<AiChatResponse> reply) => Then(_ => reply);

        public ScriptedChatClient Then(Func<AiChatRequest, Result<AiChatResponse>> reply)
        {
            _replies.Enqueue(reply);
            return this;
        }

        public Task<Result<AiChatResponse>> CompleteAsync(AiChatRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return _replies.Count == 0
                ? throw new InvalidOperationException($"Unscripted chat call #{Requests.Count}.")
                : Task.FromResult(_replies.Dequeue()(request));
        }

        public Task<Result<IReadOnlyList<ChatModelDescriptor>>> ReadFreeModelsAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<ChatModelHealth>> ReadModelHealthAsync(string modelId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    /// <summary>The daily allowance as one counter, claimed the way the EF repository claims it.</summary>
    private sealed class InMemoryBudget : IAiBudgetRepository
    {
        public int Cap { get; set; } = 100;

        public int Spent { get; set; }

        public Task<bool> TryReserveRequestsAsync(DateOnly dateUtc, int amount, int dailyCap, CancellationToken cancellationToken = default)
        {
            if (Spent + amount > Math.Min(Cap, dailyCap))
            {
                return Task.FromResult(false);
            }

            Spent += amount;
            return Task.FromResult(true);
        }

        public Task ReleaseRequestsAsync(DateOnly dateUtc, int amount, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task RecordTokenUsageAsync(DateOnly dateUtc, int promptTokens, int completionTokens, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<AiBudgetSnapshot> ReadAsync(DateOnly dateUtc, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AiBudgetSnapshot(dateUtc, Spent, 0, 0));
    }
}
