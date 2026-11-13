using FluentAssertions;
using UseCases.OutputPorts.AI;
using UseCases.UseCases.AI.Tools;
using Xunit;

namespace GeoClubBot.Tests.AI;

/// <summary>The region_map tool: how its arguments are read, and what the model is told of its picture.</summary>
public sealed class RegionMapArgumentsTests
{
    [Fact]
    public void ReadsACountryAndRegionsWithShares()
    {
        var ok = ToolArguments.TryReadRegionMap(
            """{"country": "Vietnam", "regions": [{"name": "Lạng Sơn", "share": 0.7}, {"name": "Quảng Ninh", "share": "30%"}, "Hanoi"]}""",
            out var arguments, out var error);

        ok.Should().BeTrue(error);
        arguments.Country.Should().Be("Vietnam");
        arguments.Regions.Should().Equal(new RegionShare("Lạng Sơn", 0.7), new RegionShare("Quảng Ninh", 0.3), new RegionShare("Hanoi", 1d));
    }

    [Fact]
    public void ReadsAPercentage_AsAFraction()
    {
        ToolArguments.TryReadRegionMap("""{"country": "Ghana", "regions": [{"name": "Ashanti", "share": 80}]}""", out var arguments, out _).Should().BeTrue();

        arguments.Regions.Single().Share.Should().Be(0.8);
    }

    [Theory]
    [InlineData("""{"regions": ["Sabah"]}""", "'country' is required")]
    [InlineData("""{"country": "Malaysia"}""", "'regions' must be a list")]
    [InlineData("""{"country": "Malaysia", "regions": "Sabah"}""", "'regions' must be a list")]
    [InlineData("""{"country": "Malaysia", "regions": [{"share": 0.5}]}""", "names no region")]
    public void RefusesWhatItCannotRead_AndSaysWhy(string json, string reason)
    {
        ToolArguments.TryReadRegionMap(json, out _, out var error).Should().BeFalse();

        error.Should().Contain(reason).And.Contain("region_map");
    }

    [Fact]
    public void TheToolIsDeclared_OnlyWhenOn()
    {
        AnswerTools.Definitions(speciesRange: true, regionMap: true).Select(tool => tool.Name).Should().Contain(AnswerTools.RegionMap);
        AnswerTools.Definitions(speciesRange: true).Select(tool => tool.Name).Should().NotContain(AnswerTools.RegionMap);
        AnswerTools.Names.Should().Contain(AnswerTools.RegionMap, "a call of it written as text is not an answer either");
    }

    [Fact]
    public void TheExcerpt_IsAPicture_ThatSaysWhatIsShaded()
    {
        var hit = RegionMapExcerpt.ToHit("VN", "Vietnam",
            [new RegionHighlight("VN-37", "Lạng Sơn", 0.7), new RegionHighlight("VN-13", "Quảng Ninh", 0.3)],
            "https://bot.example/api/v1/ai/images/map.jpg", shaded: true);

        hit.Kind.Should().Be(KnowledgeChunkKind.Image);
        hit.ImageUrl.Should().Be("https://bot.example/api/v1/ai/images/map.jpg");
        hit.Title.Should().Be("Region map");
        hit.SectionPath.Should().Be("Lạng Sơn and Quảng Ninh in Vietnam");
        hit.Text.Should().Be("A map of Vietnam with Lạng Sơn (70 %), Quảng Ninh (30 %) shaded red, darker where the share is higher.");
    }

    [Theory]
    [InlineData(new[] { "Sabah" }, "Sabah in Malaysia")]
    [InlineData(new[] { "Sabah", "Sarawak" }, "Sabah and Sarawak in Malaysia")]
    [InlineData(new[] { "Sabah", "Sarawak", "Johor" }, "Sabah, Sarawak and Johor in Malaysia")]
    [InlineData(new string[0], "Regions of Malaysia")]
    public void TheTitle_ListsTheRegions(string[] regions, string expected) =>
        RegionMapExcerpt.Title("Malaysia", regions).Should().Be(expected);
}
