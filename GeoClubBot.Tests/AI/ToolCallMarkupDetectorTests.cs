using FluentAssertions;
using UseCases.UseCases.AI.Conversations;
using UseCases.UseCases.AI.Tools;
using Xunit;

namespace GeoClubBot.Tests.AI;

/// <summary>
/// Every reply recognised here is a shape a model wrote in place of an answer; the first six are the
/// exact replies nemotron-3-ultra gave after its last tool round in the 2026-10-29 answer replay, which
/// were posted as answers under "This answer cites no guide".
/// </summary>
public sealed class ToolCallMarkupDetectorTests
{
    [Theory]
    [InlineData("<tool_call>\n<function=search_guides>\n<parameter=country>\nPhilippines\n</parameter>\n<parameter=query>\nrice paddy Philippines agriculture\n</parameter>\n</function>\n</tool_call>")]
    [InlineData("<tool_call>\n<function=read_guide>\n<parameter=guide_id>\n1\n</parameter>\n</function>\n</tool_call>")]
    [InlineData("<tool_call>\n<function=search_guides>\n<parameter=country>\nUSA\n</parameter>\n<parameter=query>\nMUTCD stop sign plaque ALL WAY 4-WAY\n</parameter>\n</function>\n</tool_call>")]
    [InlineData("<tool_call>\n<function=read_guide>\n<parameter=excerpt>\n2\n</parameter>\n</function>\n</tool_call>")]
    [InlineData("<tool_call>\n<function=search_guides>\n<parameter=query>\nKosovo\n</parameter>\n</function>\n</tool_call>")]
    [InlineData("<tool_call>\n<function=search_guides>\n<parameter=query>\ncountries with Generation 2 coverage list\n</parameter>\n</function>\n</tool_call>")]
    public void RecognisesTheRepliesPostedAsAnswers_In_The_2026_10_29_Replay(string reply) =>
        ToolCallMarkupDetector.IsToolCallMarkup(reply, AnswerTools.Names).Should().BeTrue();

    [Theory]
    [InlineData("Let me look that up.\n<tool_call>\n{\"name\": \"search_guides\", \"arguments\": {\"query\": \"rice\"}}\n</tool_call>")]
    [InlineData("</tool_call>")]
    [InlineData("<function=search_guides>{\"query\": \"rice\"}</function>")]
    [InlineData("<parameter=query>rice</parameter>")]
    [InlineData("<|tool_calls_section_begin|><|tool_call_begin|>functions.search_guides:0<|tool_call_argument_begin|>{\"query\":\"rice\"}<|tool_call_end|><|tool_calls_section_end|>")]
    [InlineData("<｜tool▁calls▁begin｜><｜tool▁call▁begin｜>function<｜tool▁sep｜>search_guides")]
    [InlineData("<|tool_call|>search_guides")]
    [InlineData("[TOOL_CALLS][{\"name\": \"search_guides\", \"arguments\": {\"query\": \"rice\"}}]")]
    public void RecognisesTheTokensChatTemplatesWriteCallsWith(string reply) =>
        ToolCallMarkupDetector.IsToolCallMarkup(reply, AnswerTools.Names).Should().BeTrue();

    [Theory]
    [InlineData("{\"name\": \"search_guides\", \"arguments\": {\"query\": \"rice\", \"country\": \"Philippines\"}}")]
    [InlineData("```json\n{\"name\": \"read_guide\", \"parameters\": {\"excerpt\": 2}}\n```")]
    [InlineData("```\n[{\"name\": \"search_guides\", \"arguments\": {\"query\": \"rice\"}}, {\"name\": \"species_range\", \"arguments\": {\"name\": \"Oryza sativa\"}}]\n```")]
    [InlineData("{\"type\": \"function\", \"function\": {\"name\": \"search_guides\", \"arguments\": \"{\\\"query\\\": \\\"rice\\\"}\"}}")]
    [InlineData("{\"tool_calls\": [{\"id\": \"c1\", \"function\": {\"name\": \"species_range\", \"arguments\": {\"name\": \"khasi pine\"}}}]}")]
    [InlineData("  {\"tool\": \"SEARCH_GUIDES\", \"args\": {\"query\": \"rice\"}}  ")]
    public void RecognisesAReplyThatIsNothingButAJsonCall(string reply) =>
        ToolCallMarkupDetector.IsToolCallMarkup(reply, AnswerTools.Names).Should().BeTrue();

    [Theory]
    [InlineData("The guides don't say where rice grows; search_guides found only Thai excerpts.")]
    [InlineData("I could call read_guide on [2], but the excerpt already lists the provinces [2].")]
    [InlineData("{\"name\": \"web_search\", \"arguments\": {\"query\": \"rice\"}}")]
    [InlineData("{\"name\": \"search_guides\"}")]
    [InlineData("{\"answer\": \"Rice grows in Central Luzon.\"}")]
    [InlineData("```json\n{\"query\": \"rice\"}\n```")]
    [InlineData("Poles with a <b>bold</b> stripe are a function of the region, x <= 3.")]
    [InlineData("[1] Rice terraces are common in the Cordillera [1].")]
    [InlineData("")]
    [InlineData("   ")]
    public void PassesAnAnswer_ThatOnlyMentionsATool(string reply) =>
        ToolCallMarkupDetector.IsToolCallMarkup(reply, AnswerTools.Names).Should().BeFalse();

    [Fact]
    public void ReadsANullReply_AsNoCall() =>
        ToolCallMarkupDetector.IsToolCallMarkup(null, AnswerTools.Names).Should().BeFalse();

    private const string Call = "{\"name\": \"search_guides\", \"arguments\": {}}";

    [Theory]
    [InlineData("{\"tool_name\": \"search_guides\", \"arguments\": {}}")]
    [InlineData("{\"function\": \"search_guides\", \"arguments\": {}}")]
    [InlineData("{\"name\": \"search_guides\", \"input\": {}}")]
    [InlineData("{\"tool_call\": " + Call + "}")]
    [InlineData("{\"function_call\": " + Call + "}")]
    [InlineData("[[[" + Call + "]]]")]
    [InlineData("{\"tool_calls\": {\"tool_calls\": {\"tool_calls\": " + Call + "}}}")]
    public void RecognisesEveryKeyACallIsWrittenWith_AndWrappersUpToTheirDepth(string reply) =>
        ToolCallMarkupDetector.IsToolCallMarkup(reply, AnswerTools.Names).Should().BeTrue();

    [Theory]
    [InlineData("[]")]
    [InlineData("[" + Call + ", \"and more\"]")]
    [InlineData("[[[[[" + Call + "]]]]]")]
    [InlineData("{\"tool_calls\": {\"tool_calls\": {\"tool_calls\": {\"tool_calls\": {\"tool_calls\": " + Call + "}}}}}")]
    [InlineData("{\"tool_calls\": \"search_guides\", \"arguments\": {}}")]
    [InlineData("{\"name\": 5, \"arguments\": {}}")]
    [InlineData("{not json")]
    [InlineData("```\n\n```")]
    public void PassesJson_ThatIsNotACallThroughAndThrough(string reply) =>
        ToolCallMarkupDetector.IsToolCallMarkup(reply, AnswerTools.Names).Should().BeFalse();

    [Fact]
    public void NeedsToKnowTheTools()
    {
        var act = () => ToolCallMarkupDetector.IsToolCallMarkup("x", null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
