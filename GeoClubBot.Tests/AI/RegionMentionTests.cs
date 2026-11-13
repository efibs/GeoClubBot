using FluentAssertions;
using UseCases.OutputPorts.AI;
using UseCases.UseCases.AI.Tools;
using Xunit;

namespace GeoClubBot.Tests.AI;

/// <summary>Which of a country's regions an answer names: accents and case aside, whole words, the longest name winning.</summary>
public sealed class RegionMentionTests
{
    private static readonly IReadOnlyList<RegionName> Vietnam =
    [
        new("VN-37", "Lạng Sơn", ["Lang Son"]),
        new("VN-13", "Quảng Ninh", ["Quang Ninh"]),
        new("VN-HN", "Hanoi", ["Hà Nội", "Ha Noi"]),
        new("VN-24", "Nghệ An", []),
        new("VN-01", "Lai Châu", [])
    ];

    [Fact]
    public void Find_MatchesNamesWithOrWithoutAccents_InTheOrderTheyAppear()
    {
        var found = RegionMention.Find(
            "The pattern is seen in far north-eastern Vietnam: Lang Son and Quảng Ninh, less so around Hanoi.", Vietnam);

        found.Select(region => region.Name).Should().Equal("Lạng Sơn", "Quảng Ninh", "Hanoi");
    }

    [Fact]
    public void Find_MatchesWholeWordsOnly()
    {
        var regions = new List<RegionName> { new("ID-SU", "North Sumatra", []), new("ID-SB", "West Sumatra", []), new("X", "Riau", []) };

        RegionMention.Find("mostly in North Sumatra and the Riau islands", regions).Select(region => region.Name)
            .Should().Equal("North Sumatra", "Riau");
    }

    [Fact]
    public void Find_PrefersTheLongerName_WhereTwoOverlap()
    {
        var regions = new List<RegionName> { new("A", "Sumatra", []), new("B", "North Sumatra", []) };

        RegionMention.Find("found in North Sumatra", regions).Select(region => region.Name).Should().Equal("North Sumatra");
    }

    [Fact]
    public void Find_TakesAGenericName_OnlyBesideAWordThatMakesItARegion()
    {
        var ghana = new List<RegionName> { new("GH-CP", "Central", []), new("GH-NP", "Northern", []), new("GH-AH", "Ashanti", []) };

        RegionMention.Find("The central part of the country, mostly Ashanti.", ghana).Select(region => region.Name).Should().Equal("Ashanti");
        RegionMention.Find("The Central Region and the Northern Region of Ghana.", ghana).Select(region => region.Name)
            .Should().Equal("Central", "Northern");
        RegionMention.Find("in the province of Central", ghana).Select(region => region.Name).Should().Equal("Central");
    }

    [Fact]
    public void Find_IgnoresNamesTooShortToBeSafe()
    {
        var regions = new List<RegionName> { new("A", "Ba", []), new("B", "Utah", []) };

        RegionMention.Find("Ba and Utah", regions).Select(region => region.Name).Should().Equal("Utah");
    }

    [Fact]
    public void Find_NamesARegionOnce_HoweverOftenItIsWritten() =>
        RegionMention.Find("Hanoi, Hà Nội, Ha Noi", Vietnam).Should().ContainSingle();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("nothing here")]
    public void Find_FindsNothing_InNothing(string? text) =>
        RegionMention.Find(text, Vietnam).Should().BeEmpty();

    [Theory]
    [InlineData("Lạng Sơn", "lang son")]
    [InlineData("Đà Nẵng", "da nang")]
    [InlineData("São Paulo", "sao paulo")]
    [InlineData("Baden-Württemberg", "baden wurttemberg")]
    [InlineData("  Quảng   Ninh ", "quang ninh")]
    [InlineData("Łódź", "lodz")]
    public void Normalise_DropsAccentsCaseAndPunctuation(string text, string expected) =>
        RegionMention.Normalise(text).Should().Be(expected);
}
