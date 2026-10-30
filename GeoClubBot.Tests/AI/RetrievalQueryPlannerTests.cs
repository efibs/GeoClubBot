using Entities;
using FluentAssertions;
using UseCases.OutputPorts.AI;
using UseCases.UseCases.AI.Conversations;
using UseCases.UseCases.AI.Retrieval;
using Xunit;

namespace GeoClubBot.Tests.AI;

/// <summary>
/// The planner's prompt and the reader of its reply. Free models wrap the JSON they are asked for in
/// whatever they like, and a reply that cannot be read must only mean the question is searched without
/// a plan — so the reader is pinned against the shapes they were seen to send.
/// </summary>
public sealed class RetrievalQueryPlannerTests
{
    [Fact]
    public void Parse_ReadsTheReplyItAskedFor()
    {
        // Replied by dots-3-note-preview on 2026-10-24 to "What about corn?" after a sugar-cane question.
        var plan = RetrievalQueryPlanner.Parse(
            "\n\n{\"question\": \"Where can corn be found in the Philippines?\", \"searches\": [\"corn growing regions Philippines\", "
            + "\"maize cultivation areas\"], \"countries\": [\"Philippines\"], \"phrases\": [\"corn fields\"]}",
            maxPhrasings: 3);

        plan.Should().NotBeNull();
        plan!.StandaloneQuestion.Should().Be("Where can corn be found in the Philippines?");
        plan.SearchPhrasings.Should().Equal("corn growing regions Philippines", "maize cultivation areas");
        plan.Countries.Should().Equal("Philippines");
        plan.KeyPhrases.Should().Equal("corn fields");
    }

    [Theory]
    [InlineData("Sure! Here is the plan:\n```json\n{\"question\": \"Where is corn grown in the Philippines?\"}\n```\nHope it helps.")]
    [InlineData("<think>The user asks about {corn}. I should answer with JSON.</think>{\"question\": \"Where is corn grown in the Philippines?\"}")]
    [InlineData("{\"question\": \"Where is corn grown in the Philippines?\", \"searches\": [],}")]
    [InlineData("I think {this} is the answer: {\"question\": \"Where is corn grown in the Philippines?\"}")]
    [InlineData("{\"question\": \"Where is corn grown in the Philippines?\"}\nNote: I left out {phrases} as unsure.")]
    [InlineData("{\"question\": \"Where is corn grown in the Philippines?\", \"phrases\": [\"a } brace\"]} and {more}")]
    public void Parse_FindsTheObject_InWhateverAModelWrapsItIn(string reply) =>
        RetrievalQueryPlanner.Parse(reply, maxPhrasings: 3)!.StandaloneQuestion
            .Should().Be("Where is corn grown in the Philippines?");

    [Fact]
    public void Parse_TakesASingleStringWhereAListWasAskedFor_AndOtherNamesForTheFields()
    {
        var plan = RetrievalQueryPlanner.Parse(
            "{\"standalone_question\": \"Snow poles in Finland?\", \"queries\": \"snow poles Finland\", \"country\": \"Finland\"}",
            maxPhrasings: 3);

        plan!.StandaloneQuestion.Should().Be("Snow poles in Finland?");
        plan.SearchPhrasings.Should().Equal("snow poles Finland");
        plan.Countries.Should().Equal("Finland");
    }

    [Fact]
    public void Parse_KeepsAtMostWhatItWasAskedFor_AndDropsSentencesPosingAsPhrases()
    {
        var plan = RetrievalQueryPlanner.Parse(
            """
            {"question": "q", "searches": ["one", "two", "three", "four"],
             "countries": ["a", "b", "c", "d", "e", "f"],
             "phrases": ["no supports", "this whole sentence is not a phrase at all", "single insulator", "x", "y"]}
            """,
            maxPhrasings: 2);

        plan!.SearchPhrasings.Should().Equal("one", "two");
        plan.Countries.Should().HaveCount(RetrievalQueryPlanner.MaxCountries);
        plan.KeyPhrases.Should().Equal("no supports", "single insulator", "x");
    }

    [Fact]
    public void Parse_CleansAndDeduplicatesWhatItReads()
    {
        var plan = RetrievalQueryPlanner.Parse(
            "{\"question\": \"  snow   poles \\n in Norway \", \"searches\": [\"Snow poles\", \"snow poles\", \"\", 3]}",
            maxPhrasings: 3);

        plan!.StandaloneQuestion.Should().Be("snow poles in Norway");
        plan.SearchPhrasings.Should().Equal("Snow poles");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("I'm sorry, I can't help with that.")]
    [InlineData("User Safety: safe")]
    [InlineData("[\"a list\", \"not an object\"]")]
    [InlineData("{\"question\": \"\", \"searches\": [], \"countries\": [], \"phrases\": []}")]
    [InlineData("{\"answer\": \"Corn is grown in Mindanao.\"}")]
    [InlineData("{\"question\": \"unterminated")]
    public void Parse_ReturnsNothing_WhenThereIsNothingToSearchBy(string? reply) =>
        RetrievalQueryPlanner.Parse(reply, maxPhrasings: 3).Should().BeNull();

    [Fact]
    public void Parse_RejectsAWordingLongEnoughToBeAnAnswer() =>
        RetrievalQueryPlanner.Parse($"{{\"question\": \"{new string('a', 400)}\"}}", maxPhrasings: 3)
            .Should().BeNull("a model answering instead of rewriting is no search");

    [Fact]
    public void BuildMessages_GivesTheConversation_WithoutTheAnswersCitations()
    {
        // The planner never sees the excerpts an earlier answer cited, so "[1]" would only puzzle it.
        var turns = new List<ConversationTurnView>
        {
            new(AiTurnRole.User, 1, "where can I find sugar cane in the Philippines?", []),
            new(AiTurnRole.Assistant, 2, "Mostly on Negros [1] and in northern Luzon [2].", [])
        };

        var messages = RetrievalQueryPlanner.BuildMessages(turns, "What about corn?", hasScreenshot: false);

        messages.Should().HaveCount(2);
        messages[0].Role.Should().Be(AiChatRole.System);
        messages[0].ToPlainText().Should().Be(RetrievalQueryPlanner.SystemPrompt);

        var user = messages[1].ToPlainText();
        user.Should().Contain("User: where can I find sugar cane in the Philippines?");
        user.Should().Contain("Assistant: Mostly on Negros and in northern Luzon.");
        user.Should().NotContain("[1]");
        user.Should().EndWith("Latest question: What about corn?");
    }

    [Fact]
    public void BuildMessages_SendsTextOnly_AndSaysThereIsAPictureItCannotSee()
    {
        var turns = new List<ConversationTurnView> { new(AiTurnRole.User, 1, "is this Bengali?", ["https://cdn/sign.png"]) };

        var messages = RetrievalQueryPlanner.BuildMessages(turns, "Where is this?", hasScreenshot: true);

        messages.SelectMany(message => message.Parts).Should().AllBeOfType<AiTextPart>(
            "the planner rewrites words, and the pool of models that read pictures is far smaller");
        messages[1].ToPlainText().Should().Contain("screenshot, which you cannot see");
    }

    [Fact]
    public void BuildMessages_KeepsOnlyTheRecentConversation_Shortened()
    {
        var turns = Enumerable.Range(0, 10)
            .Select(index => new ConversationTurnView(
                index % 2 == 0 ? AiTurnRole.User : AiTurnRole.Assistant, 1, $"turn {index} " + new string('x', 1000), []))
            .ToList();

        var user = RetrievalQueryPlanner.BuildMessages(turns, "and now?", hasScreenshot: false)[1].ToPlainText();

        user.Should().NotContain("turn 3 ", "only the latest turns say what the thread is about");
        user.Should().Contain("turn 4 ").And.Contain("turn 9 ");
        user.Length.Should().BeLessThan(6 * 420 + 200);
    }

    [Fact]
    public void BuildMessages_ForAFreshQuestion_IsTheQuestionAlone() =>
        RetrievalQueryPlanner.BuildMessages([], "where can I find Khasi pines?", hasScreenshot: false)[1].ToPlainText()
            .Should().Be("Latest question: where can I find Khasi pines?");
}
