using System.IO.Compression;
using System.Net;
using System.Text;
using Configuration;
using FluentAssertions;
using Infrastructure.OutputAdapters.AI.Extractors;
using Infrastructure.OutputAdapters.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using UseCases.OutputPorts.AI;
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

    [Theory]
    [InlineData("""{"data":{"count":0,"images":[]}}""")]
    [InlineData("""{"data":[],"success":true,"status":200}""")]
    public async Task Imgur_ReportsAnEmptyAlbumAsGone(string payload)
    {
        // Deleted and private albums answer this way and will keep doing so — the second payload is
        // what imgur measurably returns, with a 200, for an album id that does not exist. They must not
        // be retried every night, nor skipped for good, which kept a dead album's pictures cited for ever.
        var extractor = new ImgurAlbumSourceExtractor(Factory(payload), NullLogger<ImgurAlbumSourceExtractor>.Instance);

        var result = await extractor.ExtractAsync(ImgurAlbum);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
        result.Error.Code.Should().Be(SourceExtractionErrorCodes.Gone);
    }

    [Fact]
    public async Task GoogleDoc_SplitsTextIntoParagraphsAndTracksHeadings()
    {
        var extractor = CreateDocExtractor(
            Factory("Tunisian Driving Directions\n\nRoads here are numbered oddly, which matters.\n\nCopyright\n\nOnly 2022 and 2023 appear in this country."));

        var result = await extractor.ExtractAsync(GoogleDoc());

        result.IsSuccess.Should().BeTrue();
        result.Value.Chunks.Should().HaveCount(2, "headings label sections rather than becoming chunks");
        result.Value.Chunks[0].SectionPath.Should().Contain("Tunisian Driving Directions");
        result.Value.Chunks[1].SectionPath.Should().Contain("Copyright");
    }

    [Fact]
    public async Task GoogleDoc_ReportsAPrivateDocumentAsNotPublic()
    {
        // Not a skip: skipping is for good, and an owner who shares the document again expects it back.
        // Reported as its own kind so the run can check it again rarely instead of nightly or never.
        var extractor = CreateDocExtractor(Factory("Forbidden", HttpStatusCode.Forbidden));

        var result = await extractor.ExtractAsync(GoogleDoc());

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Forbidden);
        result.Error.Code.Should().Be(SourceExtractionErrorCodes.NotPublic);
    }

    public static TheoryData<string, HttpStatusCode> DeletedSources() => new()
    {
        // Production's nine nightly failures: every Google endpoint answers 410 for a deleted file. Docs
        // took it as worth retrying and 404 as "not public"; the GetStringAsync callers could not see a
        // status at all, so a deleted imgur album was retried every few nights for ever.
        { "gdoc", HttpStatusCode.Gone },
        { "gdoc", HttpStatusCode.NotFound },
        { "gslides", HttpStatusCode.Gone },
        { "gslides", HttpStatusCode.NotFound },
        { "gsheet", HttpStatusCode.Gone },
        { "gsheet", HttpStatusCode.NotFound },
        { "plonkit", HttpStatusCode.Gone },
        { "plonkit", HttpStatusCode.NotFound },
        { "rmrg", HttpStatusCode.NotFound },
        { "imgur", HttpStatusCode.NotFound },
        { "imgur", HttpStatusCode.Gone }
    };

    [Theory]
    [MemberData(nameof(DeletedSources))]
    public async Task EveryExtractor_ReportsADeletedSourceAsGone(string sourceType, HttpStatusCode status)
    {
        var (extractor, source) = ExtractorFor(sourceType, Factory("<html>gone</html>", status));

        var result = await extractor.ExtractAsync(source);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
        result.Error.Code.Should().Be(SourceExtractionErrorCodes.Gone);
    }

    [Theory]
    [InlineData("gdoc")]
    [InlineData("gslides")]
    [InlineData("gsheet")]
    [InlineData("plonkit")]
    [InlineData("imgur")]
    public async Task EveryExtractor_ReportsARefusingHost_SoTheSourceIsLeftAlone(string sourceType)
    {
        // A 429 that outlasted the retries is about the host, not the guide; blaming the guide backed it off.
        var (extractor, source) = ExtractorFor(sourceType, Factory("slow down", HttpStatusCode.TooManyRequests));

        var result = await extractor.ExtractAsync(source);

        result.Error.Code.Should().Be(SourceExtractionErrorCodes.HostRefused);
    }

    [Theory]
    [InlineData("gdoc")]
    [InlineData("gslides")]
    [InlineData("plonkit")]
    [InlineData("imgur")]
    public async Task EveryExtractor_ReportsAnOpenCircuitAsTheHostRefusing(string sourceType)
    {
        // One breaker guards every guide host. Reported as each source's own failure, a Google outage that
        // opened it backed off every PlonkIt and imgur source left in the run.
        var factory = Factory(new ThrowingHandler(
            new HostRefusedRequestException("The circuit is now open.", new InvalidOperationException())));
        var (extractor, source) = ExtractorFor(sourceType, factory);

        var result = await extractor.ExtractAsync(source);

        result.Error.Code.Should().Be(SourceExtractionErrorCodes.HostRefused);
    }

    public static TheoryData<string, HttpStatusCode, string> AccessRefusals() => new()
    {
        // Google shares each file on its own, so a refusal is about that file: its owner stopped sharing it.
        { "gdoc", HttpStatusCode.Forbidden, SourceExtractionErrorCodes.NotPublic },
        { "gdoc", HttpStatusCode.Unauthorized, SourceExtractionErrorCodes.NotPublic },
        { "gslides", HttpStatusCode.Forbidden, SourceExtractionErrorCodes.NotPublic },
        { "gslides", HttpStatusCode.Unauthorized, SourceExtractionErrorCodes.NotPublic },
        { "gsheet", HttpStatusCode.Forbidden, SourceExtractionErrorCodes.NotPublic },
        { "gsheet", HttpStatusCode.Unauthorized, SourceExtractionErrorCodes.NotPublic },

        // The guide sites and imgur publish everything, so a refusal is the site turning the bot away. Taken
        // as "not public", one bot block left every guide due that night unchecked for a month.
        { "plonkit", HttpStatusCode.Forbidden, SourceExtractionErrorCodes.HostRefused },
        { "plonkit", HttpStatusCode.Unauthorized, SourceExtractionErrorCodes.HostRefused },
        { "rmrg", HttpStatusCode.Forbidden, SourceExtractionErrorCodes.HostRefused },
        { "rmrg", HttpStatusCode.Unauthorized, SourceExtractionErrorCodes.HostRefused },
        { "imgur", HttpStatusCode.Forbidden, SourceExtractionErrorCodes.HostRefused },
        { "imgur", HttpStatusCode.Unauthorized, SourceExtractionErrorCodes.HostRefused }
    };

    [Theory]
    [MemberData(nameof(AccessRefusals))]
    public async Task EveryExtractor_ReadsAnAccessRefusal_ByHowItsHostShares(
        string sourceType,
        HttpStatusCode status,
        string expectedCode)
    {
        var (extractor, source) = ExtractorFor(sourceType, Factory("<html>refused</html>", status));

        var result = await extractor.ExtractAsync(source);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(expectedCode);
    }

    [Theory]
    [InlineData("gdoc")]
    [InlineData("gslides")]
    public async Task GoogleFiles_ReportTheFullExportsFailure_WhenTheTextStandingInForItIsEmpty(string sourceType)
    {
        // A file of pictures has only titles for text. Skipped as empty, it was never read again — pictures
        // already indexed included — over one timeout that used to be retried with backoff.
        var full = sourceType == "gdoc" ? "zip" : "pptx";
        var handler = new FormatHandler(
            (full, HttpStatusCode.ServiceUnavailable, Encoding.UTF8.GetBytes("try later")),
            ("txt", HttpStatusCode.OK, Encoding.UTF8.GetBytes(TitlesOnly)));
        var (extractor, source) = ExtractorFor(sourceType, Factory(handler));

        var result = await extractor.ExtractAsync(source);

        handler.RequestedFormats.Should().Equal(full, "txt");
        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Unexpected, "a failure that may pass must be retried, not skipped");
        result.Error.Code.Should().Be(SourceExtractionErrorCodes.Unreachable);
    }

    [Theory]
    [InlineData("gdoc")]
    [InlineData("gslides")]
    public async Task GoogleFiles_AreEmpty_WhenTheTextIsAllTheyWillEverGive(string sourceType)
    {
        // Too large to export every time: the text is the whole of what can be read, so an empty one settles it.
        var full = sourceType == "gdoc" ? "zip" : "pptx";
        var handler = new FormatHandler(
            (full, HttpStatusCode.RequestEntityTooLarge, Encoding.UTF8.GetBytes("too big")),
            ("txt", HttpStatusCode.OK, Encoding.UTF8.GetBytes(TitlesOnly)));
        var (extractor, source) = ExtractorFor(sourceType, Factory(handler));

        var result = await extractor.ExtractAsync(source);

        result.Error.Type.Should().Be(ErrorType.Validation);
        result.Error.Code.Should().Be("ai.document_empty");
    }

    /// <summary>The text export of a deck of pictures: a title per slide, and nothing else.</summary>
    private const string TitlesOnly = "Khasi Pine\n\nSikkim Spruce\n\nBhutan Pine";

    [Fact]
    public async Task GoogleDoc_DoesNotAskForTheText_WhenTheDocumentIsGone()
    {
        // The text export of a deleted document answers 410 just the same; asking costs a request for nothing.
        var handler = new FormatHandler(
            ("zip", HttpStatusCode.Gone, Encoding.UTF8.GetBytes("gone")),
            ("txt", HttpStatusCode.Gone, Encoding.UTF8.GetBytes("gone")));

        var result = await CreateDocExtractor(Factory(handler), Relay(enabled: true)).ExtractAsync(GoogleDoc());

        handler.RequestedFormats.Should().Equal("zip");
        result.Error.Code.Should().Be(SourceExtractionErrorCodes.Gone);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    public async Task GoogleDoc_FallsBackToTheTextExport_WhenTheArchiveFailsOnGooglesSide(HttpStatusCode status)
    {
        // The text export is a separate, far lighter build that usually still answers. Without it a
        // document whose archive never finishes in time would never be indexed at all.
        var handler = new FormatHandler(
            ("zip", status, Encoding.UTF8.GetBytes("try later")),
            ("txt", HttpStatusCode.OK, Encoding.UTF8.GetBytes("Tunisian roads\n\nAre numbered oddly.")));

        var result = await CreateDocExtractor(Factory(handler), Relay(enabled: true)).ExtractAsync(GoogleDoc());

        handler.RequestedFormats.Should().Equal("zip", "txt");
        result.IsSuccess.Should().BeTrue();
        result.Value.Chunks.Should().ContainSingle().Which.Text.Should().Be("Are numbered oddly.");
        result.Value.Degraded.Should().BeTrue("the pictures are missing this time only, so a fuller copy is worth keeping");
    }

    [Fact]
    public async Task GoogleDoc_FallsBackToTheTextExport_WhenTheArchiveTimesOut()
    {
        var handler = new FormatHandler(
            ("zip", HttpStatusCode.OK, []),
            ("txt", HttpStatusCode.OK, Encoding.UTF8.GetBytes("Tunisian roads\n\nAre numbered oddly.")))
        {
            // What HttpClient throws when its own timeout passes: a cancellation the caller did not ask for.
            Fail = format => format == "zip" ? new TaskCanceledException("The request timed out.") : null
        };

        var result = await CreateDocExtractor(Factory(handler), Relay(enabled: true)).ExtractAsync(GoogleDoc());

        handler.RequestedFormats.Should().Equal("zip", "txt");
        result.IsSuccess.Should().BeTrue();
        result.Value.Degraded.Should().BeTrue();
    }

    [Fact]
    public async Task GoogleDoc_FallsBackToTheTextExport_WhenTheArchiveIsCutOffMidDownload()
    {
        // The body of a heavy export is where the time goes, and a connection dropped there fails as an
        // I/O error rather than an HTTP one. Escaping the extractor, it ended the whole run.
        var handler = new FormatHandler(
            ("zip", HttpStatusCode.OK, []),
            ("txt", HttpStatusCode.OK, Encoding.UTF8.GetBytes("Tunisian roads\n\nAre numbered oddly.")))
        {
            Body = format => format == "zip" ? new BrokenStream() : null
        };

        var result = await CreateDocExtractor(Factory(handler), Relay(enabled: true)).ExtractAsync(GoogleDoc());

        handler.RequestedFormats.Should().Equal("zip", "txt");
        result.IsSuccess.Should().BeTrue();
        result.Value.Degraded.Should().BeTrue();
    }

    [Fact]
    public async Task GoogleDoc_IsNotDegraded_WhenTheArchiveIsTooLargeEveryTime()
    {
        // A size refusal recurs, so the text is the best this document will ever give and must be written.
        var handler = new FormatHandler(
            ("zip", HttpStatusCode.RequestEntityTooLarge, Encoding.UTF8.GetBytes("too big")),
            ("txt", HttpStatusCode.OK, Encoding.UTF8.GetBytes("Tunisian roads\n\nAre numbered oddly.")));

        var result = await CreateDocExtractor(Factory(handler), Relay(enabled: true)).ExtractAsync(GoogleDoc());

        result.Value.Degraded.Should().BeFalse();
    }

    [Fact]
    public async Task GoogleSlides_FallsBackToTheTextExport_WhenGoogleRefusesToBuildTheDeck()
    {
        // Measured on production: the Himalayas conifer deck answered 413 after 30 seconds on every run,
        // while its text export arrived in under two — and held the Khasi pine notes a 👎 asked for.
        var handler = new FormatHandler(
            ("pptx", HttpStatusCode.RequestEntityTooLarge, Encoding.UTF8.GetBytes("<html>too big</html>")),
            ("txt", HttpStatusCode.OK, Encoding.UTF8.GetBytes(DeckText)));

        var result = await CreateSlidesExtractor(Factory(handler), Relay(enabled: true)).ExtractAsync(GoogleSlides());

        handler.RequestedFormats.Should().Equal("pptx", "txt");
        result.IsSuccess.Should().BeTrue();
        result.Value.Chunks.Should().Contain(chunk => chunk.Text.Contains("Pinus Kesiya", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GoogleSlides_FallsBackToTheTextExport_WhenTheDeckIsLargerThanItsLimit()
    {
        // The deck used to be buffered whole, with no limit at all; the largest in the library is 339 MB.
        var handler = new FormatHandler(
            ("pptx", HttpStatusCode.OK, new byte[4096]),
            ("txt", HttpStatusCode.OK, Encoding.UTF8.GetBytes(DeckText)));

        var extractor = CreateSlidesExtractor(
            Factory(handler),
            Relay(enabled: true),
            new AiIngestionConfiguration { EmbedImages = true, MaxDeckExportBytes = 1024 });

        var result = await extractor.ExtractAsync(GoogleSlides());

        handler.RequestedFormats.Should().Equal("pptx", "txt");
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task GoogleSlides_FallsBackToTheTextExport_MarkedDegraded_WhenTheDeckFailsOnGooglesSide()
    {
        // A failure that may not recur: the text is worth having for a deck with nothing indexed yet, but
        // it must say so, or it would replace the pictures of one that has them.
        var handler = new FormatHandler(
            ("pptx", HttpStatusCode.OK, []),
            ("txt", HttpStatusCode.OK, Encoding.UTF8.GetBytes(DeckText)))
        {
            Fail = format => format == "pptx" ? new TaskCanceledException("The request timed out.") : null
        };

        var result = await CreateSlidesExtractor(Factory(handler), Relay(enabled: true)).ExtractAsync(GoogleSlides());

        handler.RequestedFormats.Should().Equal("pptx", "txt");
        result.IsSuccess.Should().BeTrue();
        result.Value.Degraded.Should().BeTrue();
    }

    [Fact]
    public async Task GoogleSlides_IsNotDegraded_WhenGoogleRefusesToBuildTheDeckEveryTime()
    {
        // A size refusal recurs, so the text is the best this deck will ever give and must be written.
        var handler = new FormatHandler(
            ("pptx", HttpStatusCode.RequestEntityTooLarge, Encoding.UTF8.GetBytes("too big")),
            ("txt", HttpStatusCode.OK, Encoding.UTF8.GetBytes(DeckText)));

        var result = await CreateSlidesExtractor(Factory(handler), Relay(enabled: true)).ExtractAsync(GoogleSlides());

        result.Value.Degraded.Should().BeFalse();
    }

    [Fact]
    public async Task GoogleSlides_DoesNotAskForTheText_WhenTheDeckIsGone()
    {
        var handler = new FormatHandler(
            ("pptx", HttpStatusCode.Gone, Encoding.UTF8.GetBytes("gone")),
            ("txt", HttpStatusCode.Gone, Encoding.UTF8.GetBytes("gone")));

        var result = await CreateSlidesExtractor(Factory(handler), Relay(enabled: true)).ExtractAsync(GoogleSlides());

        handler.RequestedFormats.Should().Equal("pptx");
        result.Error.Code.Should().Be(SourceExtractionErrorCodes.Gone);
    }

    [Fact]
    public async Task GoogleSlides_ReadsOnlyTheText_WhenImagesCannotBeServed()
    {
        // Without a relay the pictures have nowhere to live, so the deck's weight — a median of 19 MB per
        // library deck — would buy nothing the few kilobytes of text do not.
        var handler = new FormatHandler(
            ("pptx", HttpStatusCode.OK, new byte[16]),
            ("txt", HttpStatusCode.OK, Encoding.UTF8.GetBytes(DeckText)));

        var result = await CreateSlidesExtractor(Factory(handler), Relay(enabled: false)).ExtractAsync(GoogleSlides());

        handler.RequestedFormats.Should().Equal("txt");
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task GoogleSlides_KeysTheTextExportByPosition_WithoutSlideLinks()
    {
        // The text export marks neither slides nor notes, so a chunk can only say where in the text it was.
        var handler = new FormatHandler(("txt", HttpStatusCode.OK, Encoding.UTF8.GetBytes(DeckText)));

        var result = await CreateSlidesExtractor(Factory(handler), Relay(enabled: false)).ExtractAsync(GoogleSlides());

        result.Value.Chunks.Should().NotBeEmpty();
        result.Value.Chunks.Should().OnlyContain(chunk => chunk.LocalKey.StartsWith('p') && chunk.Anchor == null);
        result.Value.Chunks.Should().OnlyContain(chunk => chunk.ImageUrl == null);
        result.Value.Chunks.Select(chunk => chunk.LocalKey).Should().OnlyHaveUniqueItems();
        result.Value.Chunks.Should().OnlyContain(chunk =>
            chunk.SectionPath.StartsWith("Himalayas Conifer Tree Guide", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GoogleSlides_ReportsAPrivateDeckAsNotPublic()
    {
        var handler = new FormatHandler(("pptx", HttpStatusCode.Unauthorized, Encoding.UTF8.GetBytes("login")));

        var result = await CreateSlidesExtractor(Factory(handler), Relay(enabled: true)).ExtractAsync(GoogleSlides());

        handler.RequestedFormats.Should().Equal("pptx");
        result.Error.Type.Should().Be(ErrorType.Forbidden);
    }

    [Fact]
    public async Task GoogleDoc_TreatsAServerErrorAsWorthRetrying()
    {
        var extractor = CreateDocExtractor(Factory("boom", HttpStatusCode.InternalServerError));

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

    [Fact]
    public async Task GoogleDoc_UsesTheCheapTextExport_WhenImagesCannotBeServed()
    {
        // Without a relay the images have nowhere to live, so paying ~800x the bandwidth for the zip
        // would buy nothing.
        var handler = new RecordingHandler("Some text.\n\nMore text.");
        var extractor = CreateDocExtractor(Factory(handler), relayEnabled: false);

        await extractor.ExtractAsync(GoogleDoc());

        handler.LastRequestUri.Should().Contain("format=txt");
    }

    [Fact]
    public async Task GoogleDoc_FallsBackToTheTextExport_WhenTheArchiveIsTooLargeToTake()
    {
        // Measured on the live library: one guide of a few pages exports 332 MB of embedded images,
        // against ~10 KB of text. Nothing announces that size beforehand, so the download is abandoned
        // once it passes the limit — and the text, which is what the model reads, is taken instead.
        var handler = new FormatHandler(
            ("zip", HttpStatusCode.OK, new byte[4096]),
            ("txt", HttpStatusCode.OK, Encoding.UTF8.GetBytes("Tunisian roads\n\nAre numbered oddly.")));

        var extractor = CreateDocExtractor(
            Factory(handler),
            Relay(enabled: true),
            settings: new AiIngestionConfiguration { EmbedImages = true, MaxDocumentExportBytes = 1024 });

        var result = await extractor.ExtractAsync(GoogleDoc());

        handler.RequestedFormats.Should().Equal("zip", "txt");
        result.IsSuccess.Should().BeTrue();

        // "Tunisian roads" is short and unpunctuated, so the text export's heading heuristic takes it
        // as the section it labels rather than as content of its own.
        var chunk = result.Value.Chunks.Should().ContainSingle().Subject;
        chunk.Text.Should().Be("Are numbered oddly.");
        chunk.ImageUrl.Should().BeNull();
    }

    [Fact]
    public async Task GoogleDoc_FallsBackToTheTextExport_WhenGoogleRefusesToBuildTheArchive()
    {
        // Google answers 413 for an export it considers too big, after spending most of a minute on
        // it. Treated as a failure, the document was retried — and warned about — on every run.
        var handler = new FormatHandler(
            ("zip", HttpStatusCode.RequestEntityTooLarge, Encoding.UTF8.GetBytes("<html>too big</html>")),
            ("txt", HttpStatusCode.OK, Encoding.UTF8.GetBytes("Tunisian roads\n\nAre numbered oddly.")));

        var result = await CreateDocExtractor(Factory(handler), Relay(enabled: true)).ExtractAsync(GoogleDoc());

        handler.RequestedFormats.Should().Equal("zip", "txt");
        result.IsSuccess.Should().BeTrue();
        result.Value.Chunks.Should().ContainSingle().Which.Text.Should().Be("Are numbered oddly.");
    }

    [Fact]
    public async Task GoogleDoc_ReadsImagesFromTheZipExport_AndCaptionsThemFromTheirParagraph()
    {
        // An image in one of these guides is often the actual answer, and a written question reaches
        // it only through the text it sits in.
        var archive = BuildDocArchive(
            "<html><body>"
            + "<p>Tunisian plates are black with white text.<img src=\"images/image1.png\"></p>"
            + "<p>Second paragraph.</p>"
            + "</body></html>");

        var handler = new RecordingHandler(archive);
        var relay = Relay(enabled: true);
        var extractor = CreateDocExtractor(Factory(handler), relay);

        var result = await extractor.ExtractAsync(GoogleDoc());

        handler.LastRequestUri.Should().Contain("format=zip");
        result.IsSuccess.Should().BeTrue();

        var image = result.Value.Chunks.Should().ContainSingle(chunk => chunk.ImageUrl != null).Subject;
        image.ImageUrl.Should().Be("https://relay/img.png");
        image.Text.Should().Contain("black with white text");

        result.Value.Chunks.Where(chunk => chunk.ImageUrl == null).Should().HaveCount(2);
    }

    [Fact]
    public async Task GoogleDoc_FallsBackToThePrecedingParagraph_ForAnImageOnItsOwn()
    {
        var archive = BuildDocArchive(
            "<html><body>"
            + "<p>Bollards here are short and white.</p>"
            + "<p><img src=\"images/image1.png\"></p>"
            + "</body></html>");

        var result = await CreateDocExtractor(Factory(new RecordingHandler(archive)), Relay(enabled: true))
            .ExtractAsync(GoogleDoc());

        result.Value.Chunks.Single(chunk => chunk.ImageUrl != null)
            .Text.Should().Contain("short and white");
    }

    [Fact]
    public async Task GoogleDoc_DecodesEmoji_InHeadingsAndText()
    {
        // The export writes an emoji as a numeric entity, and the previous decoder turned &#128016;
        // into "&##128016;" — which production showed under answers as "Research by Yaron&##128016;".
        // (A credit like that one is no longer taken for a heading at all.)
        var archive = BuildDocArchive(
            "<html><body>"
            + "<h2>Astrakhan &#128016;</h2>"
            + "<p>Astrakhan can look like Mars &#128128; &amp; its caf&#233;s are rare.</p>"
            + "</body></html>");

        var result = await CreateDocExtractor(Factory(new RecordingHandler(archive)), Relay(enabled: true))
            .ExtractAsync(GoogleDoc());

        var chunk = result.Value.Chunks.Should().ContainSingle().Subject;
        chunk.SectionPath.Should().EndWith("Astrakhan 🐐");
        chunk.Text.Should().Be("Astrakhan can look like Mars 💀 & its cafés are rare.");
    }

    [Fact]
    public async Task GoogleDoc_TakesOnlyItsHeadingElementsForHeadings_WhenItMarksItsSectionsWithThem()
    {
        // "Usa · A Unique USA > Counties (33) ->" headed answers in the 2026-10-29 replay: a short line
        // taken for a heading.
        var archive = BuildDocArchive(
            "<html><body>"
            + "<h1>Plates</h1>"
            + "<h2>Front Plate Requirements</h2>"
            + "<p>Most states require a front plate.</p>"
            + "<p>This is just the normal</p>"
            + "<p>Ohio dropped its front plate in 2020.</p>"
            + "<h3>Image credit to PlonkIt</h3>"
            + "<p>Last words.</p>"
            + "<h2>Counties</h2>"
            + "<p>Counted by state.</p>"
            + "</body></html>");

        var result = await CreateDocExtractor(Factory(new RecordingHandler(archive)), Relay(enabled: true))
            .ExtractAsync(GoogleDoc());

        result.Value.Chunks.Select(chunk => (chunk.SectionPath, chunk.Text)).Should().Equal(
            ("Tunisia Doc > Front Plate Requirements", "Most states require a front plate."),
            ("Tunisia Doc > Front Plate Requirements", "This is just the normal"),
            ("Tunisia Doc > Front Plate Requirements", "Ohio dropped its front plate in 2020."),
            ("Tunisia Doc > Front Plate Requirements", "Image credit to PlonkIt"),
            ("Tunisia Doc > Front Plate Requirements", "Last words."),
            ("Tunisia Doc > Counties", "Counted by state."));
    }

    [Fact]
    public async Task GoogleDoc_StillReadsSectionsByTheirShape_WhenOnlyItsTitleIsAHeadingElement()
    {
        // A title styled as a heading above sections in bold: read by its styles alone, the document
        // would lose every section name.
        var archive = BuildDocArchive(
            "<html><body>"
            + "<h1>Mexico Deconstructed</h1>"
            + "<p>Boojum tree</p>"
            + "<p>Found only in the south of Baja California.</p>"
            + "</body></html>");

        var result = await CreateDocExtractor(Factory(new RecordingHandler(archive)), Relay(enabled: true))
            .ExtractAsync(GoogleDoc());

        result.Value.Chunks.Should().ContainSingle().Which.SectionPath.Should().Be("Tunisia Doc > Boojum tree");
    }

    [Fact]
    public async Task GoogleDoc_NeverTakesALinkAListItemOrACreditForAHeading_WhenItStylesNone()
    {
        var archive = BuildDocArchive(
            "<html><body>"
            + "<p>Bollards</p>"
            + "<p>Bollards are white with a red band.</p>"
            + "<p><span><a href=\"https://www.google.com/url?q=https://maps\">Counties (33)</a></span> -&gt;</p>"
            + "<p><a href=\"https://www.google.com/url?q=https://flags\">You can view the flags in more detail here</a></p>"
            + "<p>@bagaboiebailey</p>"
            + "<ul><li>Short item</li></ul>"
            + "<p>Flags differ by county.</p>"
            + "</body></html>");

        var result = await CreateDocExtractor(Factory(new RecordingHandler(archive)), Relay(enabled: true))
            .ExtractAsync(GoogleDoc());

        result.Value.Chunks.Should().OnlyContain(chunk => chunk.SectionPath == "Tunisia Doc > Bollards");
        result.Value.Chunks.Select(chunk => chunk.Text).Should().Contain(["Counties (33) ->", "@bagaboiebailey", "Short item"]);
    }

    [Fact]
    public async Task GoogleDoc_ReadsTheTextExportsHeadings_WithoutTheLinesThatOnlyLookLikeThem()
    {
        var extractor = CreateDocExtractor(Factory(
            "Tunisian roads\n\nAre numbered oddly.\n\nImage credit to PlonkIt\n\nhttps://goo.gl/maps/abc\n\n"
            + "Counties (33) ->\n\n________________\n\nSigns are blue."));

        var result = await extractor.ExtractAsync(GoogleDoc());

        result.Value.Chunks.Should().OnlyContain(chunk => chunk.SectionPath == "Tunisia Doc > Tunisian roads");
        result.Value.Chunks.Should().HaveCount(6, "a line that is no heading is still content");
    }

    [Fact]
    public async Task GoogleDoc_KeepsItsStyledHeadings_WhateverTheirLengthCaseOrAside()
    {
        // Dropped, a heading files every chunk under it under the one before: the Slovakia guide's bollard
        // paragraphs would have been told to the model as being about poles.
        var archive = BuildDocArchive(
            "<html><body>"
            + "<h1>Slovakia Guide</h1>"
            + "<h2>Poles</h2>"
            + "<p>Concrete poles with holes.</p>"
            + "<h2>Bollards : (they are also present in the Czech Republic)</h2>"
            + "<p>White with a black band and an orange reflector.</p>"
            + "<h2>blue gen 4 with antenna</h2>"
            + "<p>Seen in the east.</p>"
            + "<h2>How to tell Spain and Portugal apart by road lines</h2>"
            + "<p>Yellow outer lines are Spanish.</p>"
            + "</body></html>");

        var result = await CreateDocExtractor(Factory(new RecordingHandler(archive)), Relay(enabled: true))
            .ExtractAsync(GoogleDoc());

        result.Value.Chunks.Select(chunk => (chunk.SectionPath, chunk.Text)).Should().Equal(
            ("Tunisia Doc > Poles", "Concrete poles with holes."),
            ("Tunisia Doc > Bollards : (they are also present in the Czech Republic)", "White with a black band and an orange reflector."),
            ("Tunisia Doc > blue gen 4 with antenna", "Seen in the east."),
            ("Tunisia Doc > How to tell Spain and Portugal apart by road lines", "Yellow outer lines are Spanish."));
    }

    [Fact]
    public async Task GoogleDoc_ReadsTheTextExportsHeadings_WithAnAsideOrInLowerCase()
    {
        // The text export (and the Slides fallback, which shares it) knows headings only by their shape.
        var extractor = CreateDocExtractor(Factory(
            "Poles\n\nConcrete poles with holes.\n\n"
            + "Bollards : (they are also present in the Czech Republic)\n\nWhite with a black band.\n\n"
            + "blue gen 4 with antenna\n\nSeen in the east.\n\n"
            + "Credit unions and banks\n\nSlovenská sporiteľňa is everywhere."));

        var result = await extractor.ExtractAsync(GoogleDoc());

        result.Value.Chunks.Select(chunk => chunk.SectionPath).Should().Equal(
            "Tunisia Doc > Poles",
            "Tunisia Doc > Bollards : (they are also present in the Czech Republic)",
            "Tunisia Doc > blue gen 4 with antenna",
            "Tunisia Doc > Credit unions and banks");
    }

    [Fact]
    public async Task GoogleDoc_KeepsTheTextWhenAnImageCannotBeStored()
    {
        // A relay refusal costs a picture, never the document it belongs to.
        var archive = BuildDocArchive(
            "<html><body><p>Useful prose.<img src=\"images/image1.png\"></p></body></html>");

        var relay = Relay(enabled: true);
        relay.StoreAsync(Arg.Any<byte[]>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Result<string>.Failure(Error.Validation("ai.image_unsupported_type", "nope")));

        var result = await CreateDocExtractor(Factory(new RecordingHandler(archive)), relay)
            .ExtractAsync(GoogleDoc());

        result.IsSuccess.Should().BeTrue();
        result.Value.Chunks.Should().ContainSingle().Which.ImageUrl.Should().BeNull();
    }

    /// <summary>A zip shaped like the real export: the document as HTML plus its images as entries.</summary>
    private static byte[] BuildDocArchive(string html)
    {
        using var buffer = new MemoryStream();

        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var writer = new StreamWriter(archive.CreateEntry("Guide.html").Open()))
            {
                writer.Write(html);
            }

            using var image = archive.CreateEntry("images/image1.png").Open();
            image.Write(PngBytes);
        }

        return buffer.ToArray();
    }

    /// <summary>A one-pixel PNG with real magic bytes, so the relay's type sniffing has something honest to read.</summary>
    private static readonly byte[] PngBytes =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D,
        0x49, 0x48, 0x44, 0x52, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01
    ];

    private static IImageRelay Relay(bool enabled)
    {
        var relay = Substitute.For<IImageRelay>();
        relay.IsEnabled.Returns(enabled);
        relay.StoreAsync(Arg.Any<byte[]>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Result<string>.Success("https://relay/img.png"));

        return relay;
    }

    private static GoogleDocSourceExtractor CreateDocExtractor(
        IHttpClientFactory factory,
        IImageRelay? relay = null,
        bool relayEnabled = false,
        AiIngestionConfiguration? settings = null) =>
        new(factory,
            relay ?? Relay(relayEnabled),
            Options.Create(settings ?? new AiIngestionConfiguration { EmbedImages = true }),
            NullLogger<GoogleDocSourceExtractor>.Instance);

    /// <summary>Answers each export format differently, and remembers which were asked for.</summary>
    private sealed class FormatHandler(params (string Format, HttpStatusCode Status, byte[] Body)[] answers)
        : HttpMessageHandler
    {
        public List<string> RequestedFormats { get; } = [];

        /// <summary>An exception to throw instead of answering, by format.</summary>
        public Func<string, Exception?> Fail { get; init; } = _ => null;

        /// <summary>A body to stream instead of the canned one, by format.</summary>
        public Func<string, Stream?> Body { get; init; } = _ => null;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var uri = request.RequestUri?.ToString() ?? string.Empty;
            var format = uri.Contains("format=zip", StringComparison.Ordinal) ? "zip"
                : uri.EndsWith("/export/pptx", StringComparison.Ordinal) ? "pptx"
                : "txt";
            RequestedFormats.Add(format);

            if (Fail(format) is { } failure)
            {
                throw failure;
            }

            var answer = answers.First(candidate => candidate.Format == format);

            return Task.FromResult(new HttpResponseMessage(answer.Status)
            {
                Content = Body(format) is { } body ? new StreamContent(body) : new ByteArrayContent(answer.Body),
                RequestMessage = request
            });
        }
    }

    /// <summary>A body that breaks off part-way, as a dropped connection does.</summary>
    private sealed class BrokenStream : Stream
    {
        private bool _sentSome;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_sentSome)
            {
                throw new IOException("The response ended prematurely.");
            }

            _sentSome = true;
            buffer[offset] = 0x50;
            return 1;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class ThrowingHandler(Exception exception) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw exception;
    }

    /// <summary>An extractor of the given family, and a source it would be asked to read.</summary>
    private static (ISourceExtractor Extractor, SourceDescriptor Source) ExtractorFor(string sourceType, IHttpClientFactory factory) =>
        sourceType switch
        {
            "gdoc" => (CreateDocExtractor(factory, Relay(enabled: true)), GoogleDoc()),
            "gslides" => (CreateSlidesExtractor(factory, Relay(enabled: true)), GoogleSlides()),
            "gsheet" => (new GoogleSheetSourceExtractor(factory),
                new SourceDescriptor("gsheet", "sheet1", new Uri("https://docs.google.com/spreadsheets/d/sheet1/edit"))),
            "plonkit" => (new PlonkItSourceExtractor(factory, NullLogger<PlonkItSourceExtractor>.Instance),
                new SourceDescriptor("plonkit", "tunisia", new Uri("https://www.plonkit.net/tunisia"))),
            "rmrg" => (new RmrgSourceExtractor(factory, NullLogger<RmrgSourceExtractor>.Instance),
                new SourceDescriptor("rmrg", "greece", new Uri("https://rmrg.me/greece/"))),
            "imgur" => (new ImgurAlbumSourceExtractor(factory, NullLogger<ImgurAlbumSourceExtractor>.Instance), ImgurAlbum),
            _ => throw new ArgumentOutOfRangeException(nameof(sourceType), sourceType, null)
        };

    private static GoogleSlidesSourceExtractor CreateSlidesExtractor(
        IHttpClientFactory factory,
        IImageRelay relay,
        AiIngestionConfiguration? settings = null) =>
        new(factory,
            relay,
            Options.Create(settings ?? new AiIngestionConfiguration { EmbedImages = true }),
            NullLogger<GoogleSlidesSourceExtractor>.Instance);

    private static SourceDescriptor GoogleSlides() =>
        new("gslides", "deck1", new Uri("https://docs.google.com/presentation/d/deck1/edit"),
            Title: "Himalayas Conifer Tree Guide");

    /// <summary>Shaped like the 413 deck's real text export: a contents list, then a paragraph per slide.</summary>
    private const string DeckText =
        "Table of Contents:\nIntro, resources, disclaimers\nKhasi Pine\n\n"
        + "Khasi Pine / Pinus Kesiya, exists throughout high elevation SE Asia, with a gen 4 version in the Philippines.\n\n"
        + "Sikkim Spruce grows only in the east, above the pine belt, and is easy to tell apart.";

    private static IHttpClientFactory Factory(HttpMessageHandler handler)
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(PlonkItSourceExtractor.HttpClientName).Returns(new HttpClient(handler));
        return factory;
    }

    /// <summary>Serves a canned body and records which export format was asked for.</summary>
    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly byte[] _body;

        public RecordingHandler(string body) => _body = Encoding.UTF8.GetBytes(body);

        public RecordingHandler(byte[] body) => _body = body;

        public string LastRequestUri { get; private set; } = string.Empty;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri?.ToString() ?? string.Empty;

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(_body),
                RequestMessage = request
            });
        }
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
