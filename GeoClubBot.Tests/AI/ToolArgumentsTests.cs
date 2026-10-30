using FluentAssertions;
using UseCases.UseCases.AI.Tools;
using Xunit;

namespace GeoClubBot.Tests.AI;

/// <summary>
/// The arguments a model writes for a tool call are the model's, not ours. Each shape seen or likely —
/// a number written as a string, an object written as a JSON string, missing fields, junk — is read or
/// refused with a message the model can act on, never with an exception.
/// </summary>
public sealed class ToolArgumentsTests
{
    [Fact]
    public void Search_ReadsEveryField()
    {
        ToolArguments.TryReadSearchGuides("""{"query": "pole supports", "country": "Brazil", "pictures_only": true}""",
            out var arguments, out _).Should().BeTrue();

        arguments.Should().Be(new SearchGuidesArguments("pole supports", "Brazil", true));
    }

    [Theory]
    [InlineData("""{"Query": "poles"}""")]
    [InlineData("""{"query": "  poles  "}""")]
    [InlineData("\"{\\\"query\\\": \\\"poles\\\"}\"")]
    public void Search_AcceptsWhatAModelReasonablyMeant(string json)
    {
        ToolArguments.TryReadSearchGuides(json, out var arguments, out _).Should().BeTrue();

        arguments.Query.Should().Be("poles");
        arguments.Country.Should().BeNull();
        arguments.PicturesOnly.Should().BeFalse();
    }

    [Fact]
    public void Search_ReadsAFlagWrittenAsAString()
    {
        ToolArguments.TryReadSearchGuides("""{"query": "poles", "pictures_only": "true", "country": ""}""", out var arguments, out _)
            .Should().BeTrue();

        arguments.PicturesOnly.Should().BeTrue();
        arguments.Country.Should().BeNull("an empty country is no country");
    }

    [Theory]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("""{"query": ""}""")]
    [InlineData("""{"query": null}""")]
    [InlineData("""{"q": "poles"}""")]
    public void Search_NamesTheMissingQuery(string json)
    {
        ToolArguments.TryReadSearchGuides(json, out _, out var error).Should().BeFalse();

        error.Should().StartWith("Invalid arguments for search_guides: 'query' is required").And.Contain("""{"query": """);
    }

    [Theory]
    [InlineData("{not json", "not valid JSON")]
    [InlineData("""["poles"]""", "must be a JSON object")]
    public void Search_RefusesArgumentsThatAreNotAnObject(string json, string reason)
    {
        ToolArguments.TryReadSearchGuides(json, out _, out var error).Should().BeFalse();

        error.Should().Contain(reason);
    }

    [Fact]
    public void Search_CutsAQueryThatIsTheWholeQuestionPastedBack()
    {
        var pasted = string.Join(' ', Enumerable.Repeat("poles", 200));

        ToolArguments.TryReadSearchGuides($$"""{"query": "{{pasted}}"}""", out var arguments, out _).Should().BeTrue();

        arguments.Query.Length.Should().BeLessThanOrEqualTo(ToolArguments.MaxQueryLength);
    }

    [Theory]
    [InlineData("""{"excerpt": 3}""", 3)]
    [InlineData("""{"excerpt": "3"}""", 3)]
    [InlineData("""{"excerpt": "[3]"}""", 3)]
    [InlineData("""{"excerpt": 3.0}""", 3)]
    public void ReadGuide_ReadsTheExcerptNumber_HoweverItIsWritten(string json, int expected)
    {
        ToolArguments.TryReadReadGuide(json, out var arguments, out _).Should().BeTrue();

        arguments.Excerpt.Should().Be(expected);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"excerpt": 0}""")]
    [InlineData("""{"excerpt": -2}""")]
    [InlineData("""{"excerpt": 2.5}""")]
    [InlineData("""{"excerpt": "the third"}""")]
    public void ReadGuide_RefusesAnythingButAPositiveNumber(string json)
    {
        ToolArguments.TryReadReadGuide(json, out _, out var error).Should().BeFalse();

        error.Should().StartWith("Invalid arguments for read_guide");
    }

    [Fact]
    public void SpeciesRange_ReadsTheNameAndCountry()
    {
        ToolArguments.TryReadSpeciesRange("""{"name": "Yucca filifera", "country": "Mexico"}""", out var arguments, out _)
            .Should().BeTrue();

        arguments.Should().Be(new SpeciesRangeArguments("Yucca filifera", "Mexico"));
    }

    [Fact]
    public void SpeciesRange_NamesTheMissingName()
    {
        ToolArguments.TryReadSpeciesRange("""{"species": "Yucca filifera"}""", out _, out var error).Should().BeFalse();

        error.Should().Contain("'name' is required");
    }

    [Fact]
    public void Shorten_KeepsLogsToOneShortLine()
    {
        var shortened = ToolArguments.Shorten("{\n  \"query\": \"" + new string('x', 300) + "\"\n}", maxLength: 40);

        shortened.Should().HaveLength(40).And.StartWith("{ \"query\": \"").And.EndWith("…").And.NotContain("\n");
    }
}
