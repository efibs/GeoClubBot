using FluentAssertions;
using UseCases.UseCases.AI.Tools;
using Xunit;

namespace GeoClubBot.Tests.AI;

/// <summary>
/// Where a member says a screenshot was taken, read from their words: the most exact place the text
/// supports, settled by the rest of it, and never a plant's name or an everyday word taken for a place.
/// </summary>
public sealed class ScreenshotPlaceReaderTests
{
    [Theory]
    [InlineData("What kind of tree is this? It's from just south of Boston", "Boston, Massachusetts, United States", "south of Boston")]
    [InlineData("what tree is this, near Quincy, MA", "Quincy, Massachusetts, United States", "near Quincy")]
    [InlineData("what tree is this, near Quincy, Illinois", "Quincy, Illinois, United States", "near Quincy")]
    [InlineData("what tree is this? near Atlanta", "Atlanta, Georgia, United States", "near Atlanta")]
    [InlineData("what tree is this from Saigon", "Ho Chi Minh City, Vietnam", "from Saigon")]
    [InlineData("what tree? near Reading, England", "Reading, United Kingdom", "near Reading")]
    [InlineData("is this a pine? Boston area", "Boston, Massachusetts, United States", "Boston")]
    public void ReadsATown_AsAPoint(string question, string name, string said)
    {
        var place = Find(question);

        place.Should().NotBeNull();
        place!.Name.Should().Be(name);
        place.Precision.Should().Be(PlacePrecision.Point);
        place.Said.Should().Be(said);
        place.Latitude.Should().NotBeNull();
    }

    [Theory]
    [InlineData("what tree is this? Massachusetts", "Massachusetts, United States")]
    [InlineData("what tree is this? taken in spring in Vermont", "Vermont, United States")]
    [InlineData("what tree is this? its in montana", "Montana, United States")]
    [InlineData("what tree is this? Georgia, USA", "Georgia, United States")]
    [InlineData("what plant is this? Victoria", "Victoria, Australia")]
    public void ReadsARegion_OverATownOfTheSameName_OrAWordInLowerCase(string question, string name)
    {
        var place = Find(question);

        place.Should().NotBeNull();
        place!.Name.Should().Be(name);
        place.Precision.Should().Be(PlacePrecision.Region);
        place.Bounds.Should().NotBeNull();
    }

    [Theory]
    [InlineData("What tree is this? It's in Bulgaria", "BG")]
    [InlineData("what pine is this? somewhere in the usa", "US")]
    [InlineData("Romania, what tree is this?", "RO")]
    [InlineData("what tree is this? Georgia", "GE")]
    [InlineData("is this an aleppo pine? it's from Lebanon", "LB")]
    [InlineData("what are these trees on the left, Kenya btw", "KE")]
    public void ReadsACountry(string question, string code)
    {
        var place = Find(question);

        place.Should().NotBeNull();
        place!.CountryCode.Should().Be(code);
        place.Precision.Should().Be(PlacePrecision.Country);
    }

    [Theory]
    [InlineData("what tree is this in the picture?")]
    [InlineData("Is this a norway spruce?")]
    [InlineData("japanese maple?")]
    [InlineData("is this a mexican fan palm?")]
    [InlineData("what tree? near reading")]
    [InlineData("what tree is this? massachusetts")]
    [InlineData("Bulgaria or Romania, what tree?")]
    [InlineData("are these aleppo pines?")]
    [InlineData("Pl@ntNet said 0.52, 0.31 for these")]
    [InlineData("")]
    public void ReadsNoPlace_FromAPlantsNameAWordOrTwoCountries(string question) =>
        Find(question).Should().BeNull();

    [Fact]
    public void ReadsAMapsLink_AsAPoint_NearTheClosestTown()
    {
        var place = Find("what tree is this? https://www.google.com/maps/@42.2511,-71.0023,3a,75y,90t/data=!3m6");

        place.Should().NotBeNull();
        place!.Precision.Should().Be(PlacePrecision.Point);
        place.Latitude.Should().Be(42.2511);
        place.Longitude.Should().Be(-71.0023);
        place.CountryCode.Should().Be("US");
        place.Name.Should().Be("42.2511, -71.0023 (near Quincy, Massachusetts, United States)");
        place.ShortName.Should().Be("42.2511, -71.0023");
        place.DescribeArea(50).Should().Be("within 50 km of 42.2511, -71.0023");
    }

    [Theory]
    [InlineData("42.2511, -71.0023", 42.2511, -71.0023)]
    [InlineData("42.25 N, 71.00 W", 42.25, -71.0)]
    [InlineData("taken at 42°15'N 71°0'W", 42.25, -71.0)]
    [InlineData("-33.865, 151.209 what tree", -33.865, 151.209)]
    [InlineData("42.2511 N 71.0023 W which tree", 42.2511, -71.0023)]
    [InlineData("42.2511, 71.0023 wide trees", 42.2511, 71.0023)]
    public void ReadsCoordinates(string text, double latitude, double longitude)
    {
        var found = ScreenshotPlaceReader.FindCoordinates(text);

        found.Should().NotBeNull();
        found!.Value.Latitude.Should().BeApproximately(latitude, 0.0001);
        found.Value.Longitude.Should().BeApproximately(longitude, 0.0001);
    }

    [Fact]
    public void ReadsCoordinatesFarFromAnyTown_WithoutACountry()
    {
        var place = Find("what plant is this, 0.123, -160.456");

        place!.CountryCode.Should().BeNull();
        place.CountryArea.Should().BeNull();
        place.LocalArea(50).Should().BeOfType<UseCases.OutputPorts.AI.CircleRecordArea>();
    }

    [Fact]
    public void ReadsTheBranch_WhenTheQuestionSaysNothing_TheLatestFirst()
    {
        var place = ScreenshotPlaceReader.Find(
            "what tree is this?", PlaceFixtures.Index, IsoCountries.Gazetteer,
            ["This round was in Bulgaria", "Sorry, it was near Boston"]);

        place!.Name.Should().Be("Boston, Massachusetts, United States");
    }

    [Fact]
    public void SettlesWhichTownIsMeant_ByACountryNamedEarlierInTheBranch()
    {
        Find("what tree is this? its near Montana")!.Precision.Should().Be(PlacePrecision.Region, "the US state, with nothing else said");

        var place = ScreenshotPlaceReader.Find("what tree is this? its near Montana", PlaceFixtures.Index, IsoCountries.Gazetteer, ["This is Bulgaria"]);

        place!.CountryCode.Should().Be("BG");
    }

    [Fact]
    public void ReadsTheTownAQualifierNames_AndNotTheBiggerOne()
    {
        Find("near Boston, England")!.CountryCode.Should().Be("GB");
        Find("near Boston")!.CountryCode.Should().Be("US");
    }

    [Fact]
    public void DescribesWhereTheRecordsAreCounted()
    {
        Find("south of Boston")!.DescribeArea(50).Should().Be("within 50 km of Boston");
        Find("in Massachusetts")!.DescribeArea(50).Should().Be("in Massachusetts");
        Find("in Bulgaria")!.DescribeArea(50).Should().Be("in Bulgaria");
    }

    [Fact]
    public void KnowsNoPlace_WhenTheDatasetsAreNotRead()
    {
        ScreenshotPlaceReader.Find("near Boston", PlaceIndex.Empty, IsoCountries.Gazetteer).Should().BeNull();
        ScreenshotPlaceReader.Find("in Bulgaria", PlaceIndex.Empty, IsoCountries.Gazetteer)!.CountryCode.Should().Be("BG");
    }

    private static ScreenshotPlace? Find(string question) =>
        ScreenshotPlaceReader.Find(question, PlaceFixtures.Index, IsoCountries.Gazetteer);
}
