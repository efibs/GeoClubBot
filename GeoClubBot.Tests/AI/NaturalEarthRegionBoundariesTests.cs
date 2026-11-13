using Configuration;
using FluentAssertions;
using Infrastructure.OutputAdapters.AI.Regions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace GeoClubBot.Tests.AI;

/// <summary>
/// Reading Natural Earth's admin-1 GeoJSON: which country a region belongs to, what it is called and
/// also called, and that its outline is kept, simplified. Against a file written by the test, so no
/// download happens; the shape of the features is the published file's (read 2026-11-12).
/// </summary>
public sealed class NaturalEarthRegionBoundariesTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"ne-{Guid.NewGuid():N}");

    private const string Sample = """
        {"type":"FeatureCollection","features":[
         {"type":"Feature","properties":{"iso_a2":"VN","iso_3166_2":"VN-37","adm1_code":"VNM-5532","ne_id":1159316393,"name":"Lạng Sơn","name_en":"Lang Son","name_alt":"Langson|Lang-Son","gn_name":"Tinh Lang Son","woe_name":"Lang Son","name_local":"Lạng Sơn","longitude":106.7,"latitude":21.8},
          "geometry":{"type":"Polygon","coordinates":[[[106.0,21.5],[106.001,21.5005],[107.0,21.5],[107.0,22.0],[106.0,22.0],[106.0,21.5]]]}},
         {"type":"Feature","properties":{"iso_a2":"-1","iso_3166_2":"XK-01","adm1_code":"KOS-1","name":"Pristina","name_en":null,"longitude":21.1,"latitude":42.6},
          "geometry":{"type":"MultiPolygon","coordinates":[[[[21.0,42.5],[21.2,42.5],[21.2,42.7],[21.0,42.5]]],[[[21.3,42.5],[21.4,42.5],[21.4,42.6],[21.3,42.5]]]]}},
         {"type":"Feature","properties":{"iso_a2":"VN","iso_3166_2":"VN-13","adm1_code":"VNM-5534","name":"Quảng Ninh","name_en":"Quang Ninh"},
          "geometry":null}
        ]}
        """;

    [Fact]
    public async Task ReadsEachCountrysRegions_WithTheirNamesAndAliases()
    {
        var boundaries = CreateBoundaries(Sample);

        var vietnam = await boundaries.ReadNamesAsync("vn");

        vietnam.Should().ContainSingle("a region without a geometry cannot be drawn and is left out");
        vietnam[0].Id.Should().Be("VNM-5532");
        vietnam[0].Name.Should().Be("Lang Son", "the English name is shown");
        vietnam[0].Aliases.Should().BeEquivalentTo(["Lạng Sơn", "Tinh Lang Son", "Langson", "Lang-Son"], "every other spelling is matched, none twice");
    }

    [Fact]
    public async Task ReadsTheCountry_FromTheIso3166Code_WhereNaturalEarthHasNoAlpha2()
    {
        var boundaries = CreateBoundaries(Sample);

        var kosovo = await boundaries.ReadNamesAsync("XK");

        kosovo.Should().ContainSingle().Which.Name.Should().Be("Pristina");
    }

    [Fact]
    public async Task KeepsTheOutline_Simplified_WithItsLabelPoint()
    {
        var boundaries = CreateBoundaries(Sample);

        var shapes = await boundaries.ReadShapesAsync("VN", CancellationToken.None);

        var langSon = shapes.Single();
        langSon.Rings.Should().ContainSingle().Which.Should().HaveCount(5, "the point a few metres off the edge is straightened away");
        langSon.LabelLongitude.Should().Be(106.7);
        langSon.LabelLatitude.Should().Be(21.8);

        var pristina = (await boundaries.ReadShapesAsync("XK", CancellationToken.None)).Single();
        pristina.Rings.Should().HaveCount(2, "every part of a multipolygon is drawn");
    }

    [Fact]
    public async Task AnswersEmpty_ForACountryItDoesNotHold_AndWhenOff()
    {
        var boundaries = CreateBoundaries(Sample);
        (await boundaries.ReadNamesAsync("ZZ")).Should().BeEmpty();

        var off = CreateBoundaries(Sample, enabled: false);
        (await off.ReadNamesAsync("VN")).Should().BeEmpty();
    }

    [Fact]
    public async Task AnswersEmpty_RatherThanWaiting_WhenTheCallerIsOutOfTime()
    {
        var boundaries = CreateBoundaries(Sample);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        (await boundaries.ReadNamesAsync("VN", cancelled.Token)).Should().BeEmpty();
    }

    [Fact]
    public async Task DoesNotDownload_WhenTheFileIsThere()
    {
        var factory = Substitute.For<IHttpClientFactory>();
        var boundaries = CreateBoundaries(Sample, factory);

        await boundaries.ReadNamesAsync("VN");

        factory.DidNotReceiveWithAnyArgs().CreateClient(default!);
    }

    [Fact]
    public void Simplify_KeepsTheCornersAndDropsThePointsOnTheLines()
    {
        var ring = new List<(double, double)> { (0, 0), (0.001, 0.0005), (1, 0), (1, 1), (0.5, 1.0002), (0, 1), (0, 0) };

        NaturalEarthRegionBoundaries.Simplify(ring, 0.01).Should().Equal((0, 0), (1, 0), (1, 1), (0, 1), (0, 0));
        NaturalEarthRegionBoundaries.Simplify(ring, 0).Should().HaveCount(7, "a tolerance of zero keeps everything");
    }

    private NaturalEarthRegionBoundaries CreateBoundaries(string geoJson, IHttpClientFactory? factory = null, bool enabled = true)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, NaturalEarthRegionBoundaries.FileName), geoJson);

        var tools = new AiToolsConfiguration { RegionMap = { DataDirectory = _directory, Enabled = enabled } };
        return new NaturalEarthRegionBoundaries(
            factory ?? Substitute.For<IHttpClientFactory>(),
            Options.Create(tools),
            NullLogger<NaturalEarthRegionBoundaries>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
