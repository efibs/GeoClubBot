using Configuration;
using FluentAssertions;
using UseCases.UseCases.AI.Tools;
using UseCases.UseCases.RandomDraws;
using Xunit;

namespace GeoClubBot.Tests.Application.UseCases.RandomDraws;

public sealed class RandomDrawTests
{
    [Fact]
    public void Pick_TakesDistinctItemsOfThePool()
    {
        var drawn = RandomDraw.Pick(["a", "b", "c", "d", "e"], 3, new Random(1));

        drawn.Should().HaveCount(3).And.OnlyHaveUniqueItems().And.BeSubsetOf(["a", "b", "c", "d", "e"]);
    }

    [Fact]
    public void Pick_TakesEveryone_WhenThePoolIsSmallerThanTheCount()
    {
        RandomDraw.Pick(["a", "b"], 5, new Random(1)).Should().BeEquivalentTo(["a", "b"]);
    }

    [Fact]
    public void Pick_FromAnEmptyPool_IsEmpty()
    {
        RandomDraw.Pick(Array.Empty<string>(), 3, new Random(1)).Should().BeEmpty();
    }

    [Fact]
    public void Pick_ReachesEveryItem()
    {
        var random = new Random(7);
        var seen = Enumerable.Range(0, 500).Select(_ => RandomDraw.Pick([1, 2, 3, 4], 1, random).Single()).ToHashSet();

        seen.Should().BeEquivalentTo([1, 2, 3, 4], "a draw must not favour the start or end of the pool");
    }

    [Fact]
    public void Numbers_WithoutRepeats_AreDistinctAndInRange()
    {
        var drawn = RandomDraw.Numbers(1, 10, 10, allowRepeats: false, new Random(3));

        drawn.Should().BeEquivalentTo(Enumerable.Range(1, 10).Select(i => (long)i));
    }

    [Fact]
    public void Numbers_IncludeBothEnds()
    {
        var random = new Random(5);
        var drawn = Enumerable.Range(0, 200).SelectMany(_ => RandomDraw.Numbers(-1, 1, 1, false, random)).ToHashSet();

        drawn.Should().BeEquivalentTo([-1L, 0L, 1L]);
    }

    [Fact]
    public void Numbers_WithRepeats_MayTakeMoreThanTheRangeHolds()
    {
        RandomDraw.Numbers(4, 4, 3, allowRepeats: true, new Random(1)).Should().Equal(4, 4, 4);
    }

    [Fact]
    public void Numbers_WithoutRepeats_RefuseARangeTooSmall()
    {
        var act = () => RandomDraw.Numbers(1, 2, 3, allowRepeats: false, new Random(1));

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Dice_StayWithinTheirSides()
    {
        RandomDraw.Dice(6, 100, new Random(2)).Should().HaveCount(100).And.OnlyContain(t => t >= 1 && t <= 6);
    }

    [Fact]
    public void Teams_DealEveryoneOutEvenly()
    {
        var teams = RandomDraw.Teams(Enumerable.Range(1, 7).ToList(), 3, new Random(4));

        teams.Should().HaveCount(3);
        teams.SelectMany(t => t).Should().BeEquivalentTo(Enumerable.Range(1, 7));
        teams.Select(t => t.Count).Order().Should().Equal(2, 2, 3);
    }

    [Theory]
    [InlineData("a, b,c", new[] { "a", "b", "c" })]
    [InlineData("Fibs; Plonk ;  ; Geo", new[] { "Fibs", "Plonk", "Geo" })]
    [InlineData("one\ntwo", new[] { "one", "two" })]
    [InlineData("x, x", new[] { "x", "x" })]
    public void ParseList_SplitsAtCommasSemicolonsAndLineBreaks(string text, string[] expected)
    {
        RandomDraw.ParseList(text).Should().Equal(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" , ;")]
    public void ParseList_OfNothing_IsEmpty(string? text)
    {
        RandomDraw.ParseList(text).Should().BeEmpty();
    }
}

public sealed class DrawableCountriesTests
{
    private static readonly RandomDrawsConfiguration Defaults = new();

    [Fact]
    public void TheBuiltInStreetViewList_HoldsOnlyKnownCountries()
    {
        DrawableCountries.BuiltInStreetViewCodes.Should().OnlyContain(code => IsoCountries.IsCountry(code));
        DrawableCountries.BuiltInStreetViewCodes.Should().HaveCount(132);
    }

    [Fact]
    public void EveryStreetViewCountry_LinksToItsPlonkItGuide()
    {
        var countries = DrawableCountries.For(Defaults, streetViewOnly: true);

        countries.Should().OnlyContain(c => c.PlonkItUrl != null && c.PlonkItUrl.Host == "www.plonkit.net");
        countries.Single(c => c.Code == "US").PlonkItUrl!.ToString().Should().Be("https://www.plonkit.net/united-states");
        countries.Single(c => c.Code == "FR").Flag.Should().Be("🇫🇷");
    }

    [Fact]
    public void AllCountries_AreTheWholeTable_AndThoseWithoutAGuideHaveNoLink()
    {
        var countries = DrawableCountries.For(Defaults, streetViewOnly: false);

        countries.Should().HaveCount(IsoCountries.Codes.Count);
        countries.Single(c => c.Code == "AF").PlonkItUrl.Should().BeNull();
        countries.Select(c => c.Name).Should().BeInAscendingOrder(StringComparer.Ordinal);
    }

    [Fact]
    public void ConfiguredCodes_ReplaceTheBuiltInList_AndUnknownOnesAreDropped()
    {
        var config = new RandomDrawsConfiguration { StreetViewCountryCodes = ["fr", " DE ", "ZZ", "FR"] };

        var countries = DrawableCountries.For(config, streetViewOnly: true);

        countries.Select(c => c.Code).Should().BeEquivalentTo(["FR", "DE"]);
    }
}
