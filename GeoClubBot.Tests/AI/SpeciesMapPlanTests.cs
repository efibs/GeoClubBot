using FluentAssertions;
using Infrastructure.OutputAdapters.AI.Gbif;
using UseCases.OutputPorts.AI;
using Xunit;

namespace GeoClubBot.Tests.AI;

/// <summary>
/// What a species map shows and which tiles it is made of. A frame is the answer to "where": the
/// country asked about, or the part of the world that holds the plant.
/// </summary>
public sealed class SpeciesMapPlanTests
{
    [Fact]
    public void ACountedCountry_IsFramedWhole_AsItsMainland()
    {
        // "Where in the US": the lower 48 states, not a strip from Hawaii to Maine.
        var frame = SpeciesMapPlan.FrameOf(Range("US", [("Arizona", 42_000), ("California", 45_000)]))!.Value;

        frame.West.Should().BeInRange(-135, -124.7);
        frame.East.Should().BeInRange(-67, -58);
        frame.South.Should().BeInRange(15, 24.5);
        frame.North.Should().BeInRange(49.4, 56);
    }

    [Fact]
    public void ACountryTheTableDoesNotHold_IsNotDrawn()
    {
        SpeciesMapPlan.FrameOf(Range("ZZ", [("Somewhere", 900)])).Should().BeNull();
    }

    [Fact]
    public void ARangeCountedAcrossTheWorld_IsFramedOnTheCountriesHoldingIt()
    {
        // The Khasi pine: south-east Asia. A dozen records in New Zealand and the United States are
        // gardens, and must not make it a map of the Pacific.
        var frame = SpeciesMapPlan.FrameOf(Range(null,
            [("PH", 256), ("CN", 105), ("TH", 87), ("IN", 80), ("VN", 66), ("MM", 60), ("NZ", 12), ("US", 10)]))!.Value;

        frame.West.Should().BeInRange(50, 68.2, "India is the westernmost country holding it");
        frame.East.Should().BeInRange(134.8, 150, "China the easternmost");
        frame.Should().NotBe(SpeciesMapPlan.World);
    }

    [Fact]
    public void ARangeHeldByCountriesAWorldApart_IsFramedAsTheWorld()
    {
        SpeciesMapPlan.FrameOf(Range(null, [("AU", 900), ("US", 400), ("ZA", 300), ("PT", 200)]))
            .Should().Be(SpeciesMapPlan.World);
    }

    [Fact]
    public void ASmallCountry_IsShownWithItsNeighbours_AndALongOneIsNoStrip()
    {
        var singapore = SpeciesMapPlan.FrameOf(Range("SG", [("Singapore", 900)]))!.Value;
        singapore.Width.Should().BeGreaterThanOrEqualTo(24);
        singapore.Height.Should().BeGreaterThanOrEqualTo(14);

        var chile = SpeciesMapPlan.FrameOf(Range("CL", [("Maule", 900)]))!.Value;
        (chile.Height / chile.Width).Should().BeLessThanOrEqualTo(1.26, "Discord shows a column of a map as a sliver");
    }

    [Fact]
    public void AFrameIsCovered_ByTilesOfTheZoomThatMakesItAboutFourteenHundredPixelsWide()
    {
        var plan = SpeciesMapPlan.For(new MapBounds(-126, 23, -65, 51));

        plan.Zoom.Should().Be(3, "a tile then spans 22.5 degrees, 22.75 pixels a degree");
        plan.Tiles.Select(tile => (tile.X, tile.Y)).Should().BeEquivalentTo(
            [(2, 1), (3, 1), (4, 1), (5, 1), (2, 2), (3, 2), (4, 2), (5, 2)]);
        plan.Tiles.Should().Contain(new MapTile(3, 2, 1, 0, 0)).And.Contain(new MapTile(3, 5, 2, 1536, 512));

        // The canvas starts at 135°W, 67.5°N.
        plan.CropX.Should().Be(205);
        plan.CropY.Should().Be(375);
        plan.Width.Should().Be(1388);
        plan.Height.Should().Be(637);
    }

    [Fact]
    public void TheWholeWorld_IsDrawnFromFewTiles()
    {
        var plan = SpeciesMapPlan.For(SpeciesMapPlan.World);

        plan.Tiles.Count.Should().BeLessThanOrEqualTo(24);
        plan.Width.Should().BeGreaterThanOrEqualTo(1000);
        plan.Tiles.Should().OnlyContain(tile => plan.Shows(tile));
    }

    [Fact]
    public void AMapIsCutDownToItsRecords_WithRoomAround_AndNeverToASliver()
    {
        var plan = SpeciesMapPlan.For(new MapBounds(60, 0, 140, 56));

        // Records in a patch 300 by 40 pixels: the cut keeps a size worth looking at, and its shape.
        var tight = plan.TightenedTo(700, 600, 1000, 640);

        tight.Should().NotBeSameAs(plan);
        tight.Width.Should().BeLessThan(plan.Width);
        tight.CropX.Should().BeLessThanOrEqualTo(700);
        (tight.CropX + tight.Width).Should().BeGreaterThanOrEqualTo(1000);
        ((double)tight.Width / tight.Height).Should().BeLessThanOrEqualTo(2.21);
        tight.Frame.Width.Should().BeGreaterThanOrEqualTo(35.9);
        tight.Tiles.Should().Equal(plan.Tiles, "the same tiles, cut differently");
    }

    [Fact]
    public void AMapIsLeftAsItWas_WhenItsRecordsFillIt()
    {
        var plan = SpeciesMapPlan.For(new MapBounds(60, 0, 140, 56));

        plan.TightenedTo(plan.CropX + 20, plan.CropY + 20, plan.CropX + plan.Width - 20, plan.CropY + plan.Height - 20)
            .Should().BeSameAs(plan);
    }

    [Fact]
    public void ATileOutsideTheCut_IsNotShown()
    {
        var plan = SpeciesMapPlan.For(new MapBounds(60, 0, 140, 56)).TightenedTo(40, 40, 200, 140);

        plan.Tiles.Should().Contain(tile => !plan.Shows(tile));
        plan.Tiles.Should().Contain(tile => plan.Shows(tile));
    }

    private static SpeciesRange Range(string? countryCode, (string Name, long Records)[] areas) => new(
        5285824, "Pinus kesiya Royle ex Gordon", "Pinus kesiya", "SPECIES", MatchedByCommonName: false, FuzzyMatch: false,
        countryCode, [.. areas.Select(area => new SpeciesRangeArea(area.Name, area.Records))], areas.Sum(area => area.Records));
}
