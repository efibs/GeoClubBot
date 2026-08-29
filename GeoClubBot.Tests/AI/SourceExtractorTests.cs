using System.Net;
using System.Text;
using FluentAssertions;
using Infrastructure.OutputAdapters.AI.Extractors;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using UseCases.OutputPorts.AI.Ingestion;
using Utilities;
using Xunit;

namespace GeoClubBot.Tests.AI;

/// <summary>
/// Covers the remaining source families. Payload shapes are copied from real responses, and no test
/// touches the network.
/// </summary>
public sealed class SourceExtractorTests
{
    private static readonly SourceDescriptor ImgurAlbum =
        new("imgur", "e3RTN2O", new Uri("https://imgur.com/a/e3RTN2O"),
            Title: "District names in Bengali", Country: "Bangladesh");

    [Fact]
    public async Task Imgur_TurnsEachAlbumImageIntoAChunk()
    {
        var extractor = new ImgurAlbumSourceExtractor(
            Factory("""
                {"data":{"count":2,"images":[
                  {"hash":"zllc1ua","ext":".png","title":"Divisions","description":"By division"},
                  {"hash":"abc9999","ext":".jpg","title":null,"description":null}]},"success":true}
                """),
            NullLogger<ImgurAlbumSourceExtractor>.Instance);

        var result = await extractor.ExtractAsync(ImgurAlbum);

        result.IsSuccess.Should().BeTrue();
        result.Value.Chunks.Should().HaveCount(2);
        result.Value.Chunks.Select(chunk => chunk.ImageUrl).Should().Equal(
            "https://i.imgur.com/zllc1ua.png", "https://i.imgur.com/abc9999.jpg");

        // Keyed by the image's own hash, so a reordered album lands on the same points.
        result.Value.Chunks.Select(chunk => chunk.LocalKey).Should().Equal("zllc1ua", "abc9999");
    }

    [Fact]
    public async Task Imgur_BuildsACaptionFromEverythingKnownAboutTheImage()
    {
        // An infographic is reached by its caption, and album titles are frequently blank — so the
        // catalogue entry is often the only text there is.
        var extractor = new ImgurAlbumSourceExtractor(
            Factory("""{"data":{"images":[{"hash":"h1","ext":".png","title":"Divisions","description":"By division"}]}}"""),
            NullLogger<ImgurAlbumSourceExtractor>.Instance);

        var result = await extractor.ExtractAsync(ImgurAlbum);

        var caption = result.Value.Chunks[0].Text;
        caption.Should().Contain("Bangladesh").And.Contain("District names in Bengali")
            .And.Contain("Divisions").And.Contain("By division");
    }

    [Fact]
    public async Task Imgur_ReportsAnEmptyAlbumAsSkippable()
    {
        // Deleted and private albums answer this way and will keep doing so, so they must not be
        // retried every night.
        var extractor = new ImgurAlbumSourceExtractor(
            Factory("""{"data":{"count":0,"images":[]}}"""),
            NullLogger<ImgurAlbumSourceExtractor>.Instance);

        var result = await extractor.ExtractAsync(ImgurAlbum);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Validation);
    }

    [Fact]
    public async Task GoogleDoc_SplitsTextIntoParagraphsAndTracksHeadings()
    {
        var extractor = new GoogleDocSourceExtractor(
            Factory("Tunisian Driving Directions\n\nRoads here are numbered oddly, which matters.\n\nCopyright\n\nOnly 2022 and 2023 appear in this country."),
            NullLogger<GoogleDocSourceExtractor>.Instance);

        var result = await extractor.ExtractAsync(GoogleDoc());

        result.IsSuccess.Should().BeTrue();
        result.Value.Chunks.Should().HaveCount(2, "headings label sections rather than becoming chunks");
        result.Value.Chunks[0].SectionPath.Should().Contain("Tunisian Driving Directions");
        result.Value.Chunks[1].SectionPath.Should().Contain("Copyright");
    }

    [Fact]
    public async Task GoogleDoc_ReportsAPrivateDocumentAsSkippable()
    {
        // Not-publicly-shared is permanent, so retrying nightly would spend the run's budget failing.
        var extractor = new GoogleDocSourceExtractor(
            Factory("Forbidden", HttpStatusCode.Forbidden),
            NullLogger<GoogleDocSourceExtractor>.Instance);

        var result = await extractor.ExtractAsync(GoogleDoc());

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Validation);
        result.Error.Code.Should().Be("ai.document_not_public");
    }

    [Fact]
    public async Task GoogleDoc_TreatsAServerErrorAsWorthRetrying()
    {
        var extractor = new GoogleDocSourceExtractor(
            Factory("boom", HttpStatusCode.InternalServerError),
            NullLogger<GoogleDocSourceExtractor>.Instance);

        var result = await extractor.ExtractAsync(GoogleDoc());

        result.Error.Type.Should().Be(ErrorType.Unexpected, "a transient failure must not be marked skipped");
    }

    [Fact]
    public async Task GoogleSheet_GroupsRowsAndRepeatsTheHeader()
    {
        // A single row of a lookup table carries almost no retrievable signal; a block of them reads
        // as an answer, and each block needs the header to stay interpretable.
        var csv = "Region,Code\nDhaka,02\nChittagong,031\nKhulna,041";
        var extractor = new GoogleSheetSourceExtractor(Factory(csv));

        var result = await extractor.ExtractAsync(
            new SourceDescriptor("gsheet", "id", new Uri("https://docs.google.com/spreadsheets/d/id/edit"),
                Title: "Area codes"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Chunks.Should().ContainSingle();
        result.Value.Chunks[0].Text.Should().StartWith("Region | Code").And.Contain("Chittagong | 031");
    }

    [Fact]
    public async Task GoogleSheet_KeepsQuotedCommasAndNewlinesIntact()
    {
        // Description cells routinely contain both; splitting naively on commas would tear sentences
        // apart mid-phrase and index the fragments.
        var csv = "Name,Notes\n\"Dhaka, the capital\",\"Line one\nLine two\"";
        var extractor = new GoogleSheetSourceExtractor(Factory(csv));

        var result = await extractor.ExtractAsync(
            new SourceDescriptor("gsheet", "id", new Uri("https://docs.google.com/spreadsheets/d/id/edit")));

        result.Value.Chunks[0].Text.Should().Contain("Dhaka, the capital").And.Contain("Line one\nLine two");
    }

    [Fact]
    public async Task DirectImage_MakesOneChunkCaptionedFromTheCatalogue()
    {
        var extractor = new DirectImageSourceExtractor();

        var result = await extractor.ExtractAsync(new SourceDescriptor(
            "image", "https://i.imgur.com/x.png", new Uri("https://i.imgur.com/x.png"),
            Title: "Roof rack car map", Country: "Bangladesh", Author: "@devens"));

        result.IsSuccess.Should().BeTrue();
        var chunk = result.Value.Chunks.Should().ContainSingle().Subject;
        chunk.ImageUrl.Should().Be("https://i.imgur.com/x.png");
        chunk.Text.Should().Contain("Bangladesh").And.Contain("Roof rack car map");
    }

    private static SourceDescriptor GoogleDoc() =>
        new("gdoc", "abc123", new Uri("https://docs.google.com/document/d/abc123/edit"), Title: "Tunisia Doc");

    private static IHttpClientFactory Factory(string body, HttpStatusCode status = HttpStatusCode.OK)
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(PlonkItSourceExtractor.HttpClientName)
            .Returns(new HttpClient(new StubHandler(body, status)));

        return factory;
    }

    private sealed class StubHandler(string body, HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "text/plain"),
                RequestMessage = request
            });
    }
}
