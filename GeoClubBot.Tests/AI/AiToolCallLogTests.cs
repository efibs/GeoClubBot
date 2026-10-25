using FluentAssertions;
using UseCases.UseCases.AI.Tools;
using Xunit;

namespace GeoClubBot.Tests.AI;

public sealed class AiToolCallLogTests
{
    private static readonly IReadOnlyList<AiToolCallSummary> Calls =
    [
        new(1, AnswerTools.SearchGuides, """{"query":"snow poles","country":"Finland"}""", AiToolCallOutcome.Answered, 3, 1200, 850),
        new(2, AnswerTools.ReadGuide, """{"excerpt":4}""", AiToolCallOutcome.NothingNew, 0, 80, 40)
    ];

    [Fact]
    public void ReadsBackWhatItWrote()
    {
        AiToolCallLog.Deserialize(AiToolCallLog.Serialize(Calls)).Should().Equal(Calls);
    }

    [Fact]
    public void WritesTheOutcomeByName_InTheExportsCase()
    {
        // The archive is read by people and scripts that do not know the enum's order.
        var json = AiToolCallLog.Serialize(Calls)!;

        json.Should().Contain("\"outcome\":\"Answered\"").And.Contain("\"outcome\":\"NothingNew\"");
        json.Should().Contain("\"tool\":\"search_guides\"").And.Contain("\"newExcerpts\":3");
    }

    [Fact]
    public void WritesNothing_ForAnAnswerThatCalledNoTool()
    {
        AiToolCallLog.Serialize(null).Should().BeNull();
        AiToolCallLog.Serialize([]).Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("not json")]
    [InlineData("{\"round\":1}")]
    [InlineData("[{\"round\":1,\"tool\":\"search_guides\",\"outcome\":\"SomethingNew\"}]")]
    public void ReadsWhatItCannotUnderstand_AsNoCalls(string? json)
    {
        // A turn written by a later version must not cost an export its other records.
        AiToolCallLog.Deserialize(json).Should().BeEmpty();
    }
}
