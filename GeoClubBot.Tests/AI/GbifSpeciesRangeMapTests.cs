using System.Net;
using Configuration;
using FluentAssertions;
using Infrastructure.OutputAdapters.AI.Gbif;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using SkiaSharp;
using UseCases.OutputPorts.AI;
using Utilities;
using Xunit;

namespace GeoClubBot.Tests.AI;

/// <summary>
/// The species map against canned tiles: which tiles it asks GBIF for, what it makes of them, and that
/// nothing it meets costs the answer more than the picture.
/// </summary>
public sealed class GbifSpeciesRangeMapTests
{
    private readonly IImageRelay _relay = Substitute.For<IImageRelay>();
    private readonly AiToolsConfiguration _tools = new()
    {
        SpeciesRange = { BaseUrl = "https://gbif.test", Map = { TileBaseUrl = "https://tiles.test", BaseMapStyle = "osm-bright-en" } }
    };

    private byte[]? _stored;

    public GbifSpeciesRangeMapTests()
    {
        _relay.IsEnabled.Returns(true);
        _relay.StoreAsync(Arg.Any<byte[]>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                _stored = (byte[])call[0];
                return Result<string>.Success("https://bot.example/api/v1/ai/images/map.jpg");
            });
    }

    [Fact]
    public async Task DrawsACountry_FromItsBaseMapAndTheRecordsOverIt_AndStoresThePicture()
    {
        var handler = new TileHandler();

        var link = await Map(handler).DrawAsync(InTheUnitedStates());

        link.Should().Be("https://bot.example/api/v1/ai/images/map.jpg");
        await _relay.Received(1).StoreAsync(Arg.Any<byte[]>(), "image/jpeg", Arg.Any<CancellationToken>());

        // The lower 48 states at zoom 3: the same tiles of each layer, and no other.
        var baseMap = handler.Requests.Where(url => url.StartsWith("https://tiles.test/", StringComparison.Ordinal)).ToList();
        var records = handler.Requests.Where(url => url.StartsWith("https://gbif.test/", StringComparison.Ordinal)).ToList();

        baseMap.Should().NotBeEmpty().And.OnlyContain(url =>
            url.StartsWith("https://tiles.test/4326/omt/3/", StringComparison.Ordinal)
            && url.EndsWith("@1x.png?style=osm-bright-en", StringComparison.Ordinal));
        records.Should().HaveCount(baseMap.Count).And.OnlyContain(url =>
            url.StartsWith("https://gbif.test/v2/map/occurrence/density/3/", StringComparison.Ordinal)
            && url.Contains("srs=EPSG:4326", StringComparison.Ordinal) && url.Contains("taxonKey=7628502", StringComparison.Ordinal)
            && url.Contains("bin=hex", StringComparison.Ordinal));
        baseMap.Should().Contain("https://tiles.test/4326/omt/3/3/2@1x.png?style=osm-bright-en", "Texas");

        using var picture = SKBitmap.Decode(_stored);
        picture.Should().NotBeNull("what is stored is a picture");

        // The plan's frame, and a footer saying what it shows and whose the map is.
        var plan = SpeciesMapPlan.For(SpeciesMapPlan.FrameOf(InTheUnitedStates())!.Value);
        picture.Width.Should().Be(plan.Width);
        picture.Height.Should().BeGreaterThan(plan.Height);
        picture.GetPixel(plan.Width / 2, plan.Height / 2).Should().NotBe(SKColors.White, "the base map fills the frame");
    }

    [Fact]
    public async Task ARangeThatKeepsToOnePartOfTheWorld_IsDrawnAgainFiner_ForWhereItsRecordsAre()
    {
        // The frame of the countries holding the Khasi pine is all of China and India; its records keep
        // to one corner, which is what the map should show, at a zoom that keeps its labels sharp.
        var handler = new TileHandler { RecordsOnlyIn = url => url.Contains("/density/3/12/2@", StringComparison.Ordinal) };

        var link = await Map(handler).DrawAsync(KhasiPine());

        link.Should().NotBeNull();
        var density = handler.Requests.Where(url => url.Contains("/density/", StringComparison.Ordinal)).ToList();
        density.Should().Contain(url => url.Contains("/density/3/", StringComparison.Ordinal), "the records are read first, for the wide frame");
        density.Should().Contain(url => url.Contains("/density/4/", StringComparison.Ordinal), "and again for the frame they turn out to have");

        handler.Requests.Where(url => url.StartsWith("https://tiles.test", StringComparison.Ordinal))
            .Should().OnlyContain(url => url.Contains("/omt/4/", StringComparison.Ordinal), "the base map is fetched once, for the final frame");
    }

    [Fact]
    public async Task TheWorld_IsShownWhole()
    {
        var handler = new TileHandler { RecordsOnlyIn = url => url.Contains("/density/1/3/1@", StringComparison.Ordinal) };

        await Map(handler).DrawAsync(new SpeciesRange(
            7493935, "Eucalyptus L'Hér.", "Eucalyptus", "GENUS", false, false, null,
            [new("AU", 900), new("US", 400), new("ZA", 300), new("PT", 200)], 1_800));

        handler.Requests.Should().OnlyContain(url => url.Contains("/1/", StringComparison.Ordinal), "cut down to most of its records, eucalyptus lost the Americas");
    }

    [Fact]
    public async Task ATileWithoutRecords_IsSimplyNotDrawn()
    {
        var handler = new TileHandler { RecordsOnlyIn = _ => false };

        var link = await Map(handler).DrawAsync(InTheUnitedStates());

        link.Should().NotBeNull("GBIF answers 204 for a tile that holds nothing");
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task DrawsNothing_WhenABaseTileCannotBeHad(HttpStatusCode status)
    {
        var handler = new TileHandler { BaseStatus = status };

        var link = await Map(handler).DrawAsync(InTheUnitedStates());

        link.Should().BeNull();
        await _relay.DidNotReceiveWithAnyArgs().StoreAsync(default!, default, default);
    }

    [Fact]
    public async Task DrawsNothing_AndThrowsNothing_WhenTheTilesTakeTooLong()
    {
        var handler = new TileHandler { Failure = new TaskCanceledException("The request timed out.") };

        (await Map(handler).DrawAsync(InTheUnitedStates())).Should().BeNull();
    }

    [Fact]
    public async Task LetsTheQuestionsOwnCancellationThrough()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var draw = () => Map(new TileHandler()).DrawAsync(InTheUnitedStates(), cancelled.Token);

        await draw.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task DrawsNothing_WhenThePictureCannotBeStored()
    {
        _relay.StoreAsync(Arg.Any<byte[]>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Result<string>.Failure(Error.Validation("ai.image_too_large", "Too large.")));

        (await Map(new TileHandler()).DrawAsync(InTheUnitedStates())).Should().BeNull();
    }

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public async Task AsksForNothing_WhenTurnedOff_OrWithNowhereToKeepThePicture(bool rangeEnabled, bool mapEnabled, bool relayEnabled)
    {
        _tools.SpeciesRange.Enabled = rangeEnabled;
        _tools.SpeciesRange.Map.Enabled = mapEnabled;
        _relay.IsEnabled.Returns(relayEnabled);
        var handler = new TileHandler();

        (await Map(handler).DrawAsync(InTheUnitedStates())).Should().BeNull();
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task DrawsNothing_OfARangeWithoutRecords_OrOfACountryItCannotFrame()
    {
        var handler = new TileHandler();
        var map = Map(handler);

        (await map.DrawAsync(InTheUnitedStates() with { TotalRecords = 0 })).Should().BeNull();
        (await map.DrawAsync(InTheUnitedStates() with { CountryCode = "ZZ" })).Should().BeNull();
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task KeepsAMap_AsLongAsTheRangeItShows()
    {
        var handler = new TileHandler();
        var map = Map(handler);

        await map.DrawAsync(InTheUnitedStates());
        var asked = handler.Requests.Count;
        var again = await map.DrawAsync(InTheUnitedStates());

        again.Should().Be("https://bot.example/api/v1/ai/images/map.jpg");
        handler.Requests.Should().HaveCount(asked, "the same plant in the same country is the same map");
    }

    [Fact]
    public void TheTitleSaysWhatTheMapShows()
    {
        // The density tiles take one filter, the species, so a map framed on one country shows its
        // neighbours' records too: cholla's map of the United States is purple well into Mexico.
        GbifSpeciesRangeMap.Title(InTheUnitedStates())
            .Should().Be("Cylindropuntia: GBIF occurrence records in and around United States (purple: few, yellow: many)");
        GbifSpeciesRangeMap.Title(KhasiPine())
            .Should().Be("Pinus kesiya: GBIF occurrence records (purple: few, yellow: many)");
    }

    private GbifSpeciesRangeMap Map(TileHandler handler)
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(GbifSpeciesRangeReader.HttpClientName).Returns(_ => new HttpClient(handler, disposeHandler: false));

        return new GbifSpeciesRangeMap(factory, _relay, Options.Create(_tools), TimeProvider.System, NullLogger<GbifSpeciesRangeMap>.Instance);
    }

    private static SpeciesRange InTheUnitedStates() => new(
        7628502, "Cylindropuntia (Engelm.) F.M.Knuth", "Cylindropuntia", "GENUS", MatchedByCommonName: true, FuzzyMatch: false,
        "US", [new("California", 45_272), new("Arizona", 42_087)], 125_821);

    private static SpeciesRange KhasiPine() => new(
        5285824, "Pinus kesiya Royle ex Gordon", "Pinus kesiya", "SPECIES", MatchedByCommonName: true, FuzzyMatch: false, null,
        [new("PH", 256), new("CN", 105), new("TH", 87), new("IN", 80), new("VN", 66), new("MM", 60)], 1_061);

    /// <summary>
    /// Serves a grey base tile for every base request, and for a density request a tile with a block of
    /// records in its middle — or 204, as GBIF answers for a tile that holds none.
    /// </summary>
    private sealed class TileHandler : HttpMessageHandler
    {
        private static readonly byte[] BaseTile = Tile(new SKColor(200, 200, 200), SKRectI.Create(0, 0, 512, 512));

        private static readonly byte[] RecordsTile = Tile(new SKColor(120, 20, 140), SKRectI.Create(200, 220, 90, 60));

        public List<string> Requests { get; } = [];

        public HttpStatusCode BaseStatus { get; init; } = HttpStatusCode.OK;

        public Exception? Failure { get; init; }

        /// <summary>Which density tiles hold records; every one by default.</summary>
        public Func<string, bool> RecordsOnlyIn { get; init; } = _ => true;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var url = request.RequestUri!.AbsoluteUri;
            lock (Requests)
            {
                Requests.Add(url);
            }

            if (Failure is not null)
            {
                throw Failure;
            }

            if (url.Contains("/density/", StringComparison.Ordinal))
            {
                return Task.FromResult(RecordsOnlyIn(url)
                    ? Png(RecordsTile, request)
                    : new HttpResponseMessage(HttpStatusCode.NoContent) { RequestMessage = request });
            }

            return Task.FromResult(BaseStatus == HttpStatusCode.OK
                ? Png(BaseTile, request)
                : new HttpResponseMessage(BaseStatus) { RequestMessage = request });
        }

        private static HttpResponseMessage Png(byte[] bytes, HttpRequestMessage request) =>
            new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes), RequestMessage = request };

        private static byte[] Tile(SKColor colour, SKRectI filled)
        {
            using var bitmap = new SKBitmap(512, 512, SKColorType.Rgba8888, SKAlphaType.Premul);
            bitmap.Erase(SKColors.Transparent);
            using (var canvas = new SKCanvas(bitmap))
            using (var paint = new SKPaint())
            {
                paint.Color = colour;
                canvas.DrawRect(filled, paint);
            }

            using var image = SKImage.FromBitmap(bitmap);
            using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
            return encoded.ToArray();
        }
    }
}
