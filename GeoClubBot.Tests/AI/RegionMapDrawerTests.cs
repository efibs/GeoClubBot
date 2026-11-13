using FluentAssertions;
using Infrastructure.OutputAdapters.AI.Gbif;
using Infrastructure.OutputAdapters.AI.Regions;
using SkiaSharp;
using UseCases.OutputPorts.AI;
using Xunit;

namespace GeoClubBot.Tests.AI;

/// <summary>The map itself: framed on the country as a species map is, the regions drawn where they lie, and a picture that encodes.</summary>
public sealed class RegionMapDrawerTests
{
    [Fact]
    public void ForCountry_FramesTheCountry_AndPlacesPointsOnThePicture()
    {
        var plan = SpeciesMapPlan.ForCountry("VN")!;

        plan.Width.Should().BeGreaterThan(500);
        plan.Height.Should().BeGreaterThan(300);

        // Hanoi lies inside the picture; a point west of the frame lands left of it.
        var (hanoiX, hanoiY) = plan.ToPicture(105.85, 21.03);
        hanoiX.Should().BeInRange(0, plan.Width);
        hanoiY.Should().BeInRange(0, plan.Height);

        var (southX, southY) = plan.ToPicture(106.7, 10.8);
        southY.Should().BeGreaterThan(hanoiY, "south is further down the picture");
        southX.Should().BeGreaterThan(hanoiX, "east is further right");

        plan.ToPicture(0, 21).X.Should().BeNegative();
    }

    [Fact]
    public void ForCountry_IsNull_ForACountryTheFramesDoNotKnow() =>
        SpeciesMapPlan.ForCountry("ZZ").Should().BeNull();

    [Fact]
    public void Compose_DrawsTheRegionsOverTheTiles_IntoAJpeg()
    {
        var plan = SpeciesMapPlan.ForCountry("VN")!;
        var tiles = plan.Tiles.Select(tile => plan.Shows(tile) ? BlankTile() : []).ToList();

        var langSon = new RegionShape("VN-37", "Lạng Sơn", [],
            [[(106.0, 21.5), (107.3, 21.5), (107.3, 22.4), (106.0, 22.4), (106.0, 21.5)]], 106.6, 21.9);
        var request = new RegionMapRequest("VN", [new RegionHighlight("VN-37", "Lạng Sơn", 1d)], "Lạng Sơn in Vietnam");

        var picture = RegionMapDrawer.Compose(plan, tiles, [(request.Regions[0], langSon)], request);

        picture.Should().StartWith(new byte[] { 0xFF, 0xD8 }, "it is a JPEG");

        using var image = SKBitmap.Decode(picture);
        image.Width.Should().Be(plan.Width);
        image.Height.Should().Be(plan.Height + 38);

        // The region's middle is red; a point in the sea far from it is the tile's grey.
        var (insideX, insideY) = plan.ToPicture(106.6, 21.9);
        var inside = image.GetPixel((int)insideX + 40, (int)insideY + 30);
        inside.Red.Should().BeGreaterThan((byte)(inside.Green + 40));

        var (seaX, seaY) = plan.ToPicture(112.0, 14.0);
        var sea = image.GetPixel((int)seaX, (int)seaY);
        Math.Abs(sea.Red - sea.Green).Should().BeLessThan(12);
    }

    [Fact]
    public void Compose_ShadesByWeight_WhenAsked()
    {
        var plan = SpeciesMapPlan.ForCountry("VN")!;
        var tiles = plan.Tiles.Select(tile => plan.Shows(tile) ? BlankTile() : []).ToList();

        var strong = new RegionShape("A", "A", [], [[(106.0, 21.5), (107.3, 21.5), (107.3, 22.4), (106.0, 22.4), (106.0, 21.5)]], 106.6, 21.9);
        var faint = new RegionShape("B", "B", [], [[(106.0, 19.5), (107.3, 19.5), (107.3, 20.4), (106.0, 20.4), (106.0, 19.5)]], 106.6, 19.9);
        var request = new RegionMapRequest("VN", [new RegionHighlight("A", "A", 1d), new RegionHighlight("B", "B", 0.1)], "A and B in Vietnam", Shaded: true);

        var picture = RegionMapDrawer.Compose(plan, tiles, [(request.Regions[0], strong), (request.Regions[1], faint)], request);

        using var image = SKBitmap.Decode(picture);
        var (strongX, strongY) = plan.ToPicture(106.6, 22.2);
        var (faintX, faintY) = plan.ToPicture(106.6, 20.2);
        var strongPixel = image.GetPixel((int)strongX, (int)strongY);
        var faintPixel = image.GetPixel((int)faintX, (int)faintY);

        (strongPixel.Red - strongPixel.Green).Should().BeGreaterThan(faintPixel.Red - faintPixel.Green, "the heavier region is the redder");
    }

    /// <summary>A plain grey 512×512 PNG, standing in for a base map tile.</summary>
    private static byte[] BlankTile()
    {
        using var surface = SKSurface.Create(new SKImageInfo(SpeciesMapPlan.TileSize, SpeciesMapPlan.TileSize));
        surface.Canvas.Clear(new SKColor(200, 200, 200));
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
