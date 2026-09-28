using FluentAssertions;
using UseCases.UseCases.CountryChallenges;
using Xunit;

namespace GeoClubBot.Tests.Application.UseCases.CountryChallenges;

public sealed class StandingsImportParserTests
{
    private const string UserId = "5f1b2c3d4e5f6a7b8c9d0e1f";

    [Theory]
    [InlineData("Fibs 12", "Fibs", 12)]
    [InlineData("Fibs: 12", "Fibs", 12)]
    [InlineData("Fibs - 12 pts", "Fibs", 12)]
    [InlineData("1. Fibs — 12 points", "Fibs", 12)]
    [InlineData("3) The Map Guy 7", "The Map Guy", 7)]
    [InlineData("Some-Name 5", "Some-Name", 5)]
    [InlineData("Player 123 5", "Player 123", 5)]
    public void Parse_ReadsTheFormsAHandKeptListTakes(string line, string player, int points)
    {
        var parse = StandingsImportParser.Parse(line);

        parse.Errors.Should().BeEmpty();
        parse.Lines.Should().ContainSingle().Which.Should().Be(new StandingsImportLine(1, player, null, points));
    }

    [Theory]
    [InlineData("https://www.geoguessr.com/user/" + UserId + " 7")]
    [InlineData("https://www.geoguessr.com/de/user/" + UserId + " 7")]
    [InlineData(UserId + " 7")]
    public void Parse_TakesTheUserIdFromAProfileLinkOrABareId(string line)
    {
        var parsed = StandingsImportParser.Parse(line).Lines.Should().ContainSingle().Subject;

        parsed.UserId.Should().Be(UserId);
        parsed.IsNickname.Should().BeFalse();
        parsed.Points.Should().Be(7);
    }

    [Fact]
    public void Parse_SkipsBlankAndCommentLines_AndNumbersLinesAsPasted()
    {
        var parse = StandingsImportParser.Parse("# Season 1\r\n\r\nFibs 12\r\nAnna 9");

        parse.Errors.Should().BeEmpty();
        parse.Lines.Select(l => (l.LineNumber, l.Player)).Should().Equal((3, "Fibs"), (4, "Anna"));
    }

    [Fact]
    public void Parse_ReportsLinesWithoutPoints()
    {
        var parse = StandingsImportParser.Parse("Fibs 12\nAnna\nBert -3");

        parse.Lines.Should().ContainSingle();
        parse.Errors.Select(e => e.ToString()).Should().Equal(
            "Line 2: 'Anna' is not '<player> <points>'.",
            "Line 3: 'Bert -3' is not '<player> <points>'.");
    }

    [Fact]
    public void Parse_ReportsPointsTooLargeToCount()
    {
        StandingsImportParser.Parse("Fibs 99999999999").Errors
            .Should().ContainSingle(e => e.Message.Contains("is not a number of points"));
    }
}
