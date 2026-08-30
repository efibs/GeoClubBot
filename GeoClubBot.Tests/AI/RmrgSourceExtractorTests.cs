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
/// Runs against a guide captured from the live site, so a change to its markup shows up here rather
/// than as a quietly empty index. No network is involved.
/// </summary>
public sealed class RmrgSourceExtractorTests
{
    private static readonly SourceDescriptor Georgia =
        new("rmrg", "georgia", new Uri("https://rmrg.me/georgia/"), Country: "georgia");

    [Fact]
    public async Task Extract_ReadsEveryClueOnThePage()
    {
        var result = await CreateExtractor(await ReadFixtureAsync()).ExtractAsync(Georgia);

        result.IsSuccess.Should().BeTrue();
        result.Value.Title.Should().Be("Georgia");
        result.Value.Chunks.Should().HaveCount(65, "the captured guide lists 65 clues");
    }

    [Fact]
    public async Task Extract_ReadsTheGuidesOwnUpdateStamp()
    {
        // What lets an unchanged guide be skipped without re-embedding it. The markup's own asset
        // version numbers churn independently of the content, so hashing the body would not do.
        var result = await CreateExtractor(await ReadFixtureAsync()).ExtractAsync(Georgia);

        result.Value.SourceUpdatedAtUtc.Should().NotBeNull();
        result.Value.SourceUpdatedAtUtc!.Value.UtcDateTime.Date.Should().Be(new DateTime(2026, 8, 29));
    }

    [Fact]
    public async Task Extract_PairsEveryClueWithThePictureItDescribes()
    {
        // The pairing is the whole value of this site: a written question reaches the picture through
        // its caption, which scores far higher than matching the question against the pixels.
        var result = await CreateExtractor(await ReadFixtureAsync()).ExtractAsync(Georgia);

        result.Value.Chunks.Should().OnlyContain(chunk => chunk.ImageUrl != null);
        result.Value.Chunks.Should().OnlyContain(chunk => chunk.Text.Length > 0);
    }

    [Fact]
    public async Task Extract_TakesThePhotoRatherThanTheAnnotationsDrawnOverIt()
    {
        // An annotated clue stacks a transparent SVG of arrows and circles over a photo. Indexing the
        // overlay would store a picture of arrows pointing at nothing.
        var result = await CreateExtractor(await ReadFixtureAsync()).ExtractAsync(Georgia);

        var annotated = result.Value.Chunks.Single(chunk => chunk.LocalKey == "landscape/l-temperate-rainforest");

        annotated.ImageUrl.Should().Be(
            "https://rmrg.me/guides/georgia/images-optimized/landscape/l-temperate-rainforest.webp?v=5371");
        result.Value.Chunks.Should().NotContain(chunk => chunk.ImageUrl!.Contains("/vectors"));
    }

    [Fact]
    public async Task Extract_PrefersTheOptimisedCopyOverTheFullSizeOriginal()
    {
        // Same picture either way, but the originals reach several megabytes and the AI provider
        // fetches these URLs server-side on every embedding call.
        var result = await CreateExtractor(await ReadFixtureAsync()).ExtractAsync(Georgia);

        result.Value.Chunks.Should().OnlyContain(chunk =>
            chunk.ImageUrl!.StartsWith("https://rmrg.me/guides/georgia/images-optimized/"));
    }

    [Fact]
    public async Task Extract_BuildsSectionPathsFromTheCategoryHeadings()
    {
        var result = await CreateExtractor(await ReadFixtureAsync()).ExtractAsync(Georgia);

        var paths = result.Value.Chunks.Select(chunk => chunk.SectionPath).Distinct().ToList();

        paths.Should().Contain("Georgia > General > Topography", "a category with subcategories");
        paths.Should().Contain("Georgia > Landscape", "a category without them");
        paths.Should().Contain("Georgia > Culture & Language > Islam", "headings arrive HTML-encoded");
    }

    [Fact]
    public async Task Extract_UsesTheSitesOwnItemIds_SoKeysAndDeepLinksSurviveReExtraction()
    {
        // Point ids derive from these, and the anchor is what makes a citation land on the clue rather
        // than the top of a very long page. A positional key would turn every later chunk into a
        // duplicate the moment the guide gains an item.
        var result = await CreateExtractor(await ReadFixtureAsync()).ExtractAsync(Georgia);

        result.Value.Chunks.Select(chunk => chunk.LocalKey).Should().OnlyHaveUniqueItems();
        result.Value.Chunks.Should().Contain(chunk => chunk.LocalKey == "architecture/miscellaneous/a-svan-towers");
        result.Value.Chunks.Should().OnlyContain(chunk => chunk.Anchor == chunk.LocalKey);
    }

    [Fact]
    public async Task Extract_KeepsTheParagraphBreaksTheAuthorsMarked()
    {
        // The breaks are empty spans rather than block elements, so plain InnerText would run three
        // separate observations into one unreadable sentence.
        var result = await CreateExtractor(await ReadFixtureAsync()).ExtractAsync(Georgia);

        var topography = result.Value.Chunks.Single(chunk => chunk.LocalKey == "general/topography/g-topography");

        topography.Text.Should().StartWith("Georgia has a very interesting and complex topography.");
        topography.Text.Should().Contain("topography.\n\nThere are two large mountain ranges");
        topography.Text.Should().EndWith("using landscape alone.");
    }

    [Fact]
    public async Task Extract_KeepsBulletedCluesApart()
    {
        // Each bullet is a distinct clue. Run together they read as one sentence that says nothing.
        var result = await CreateExtractor(await ReadFixtureAsync()).ExtractAsync(Georgia);

        var busStops = result.Value.Chunks.Single(chunk => chunk.LocalKey == "infrastructure/i-bus-stops");

        busStops.Text.Should().Contain("\n- ");
        busStops.Text.Should().NotContain("  ", "collapsed whitespace, so the model sees prose not markup");
    }

    [Fact]
    public async Task Extract_ReportsAPageWithoutCluesAsSkippable()
    {
        // The sitemap carries the landing page and a daily challenge alongside the guides. These are
        // reported as a validation failure so the caller records them as skipped rather than retrying
        // them every night.
        var extractor = CreateExtractor("<html><body><h1>RMRG</h1></body></html>");

        var result = await extractor.ExtractAsync(
            new SourceDescriptor("rmrg", "dailychallenge", new Uri("https://rmrg.me/dailychallenge/")));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("ai.not_a_guide_page");
        result.Error.Type.Should().Be(ErrorType.Validation);
    }

    [Fact]
    public async Task Extract_KeepsAsideNotesWithTheClueTheyQualify()
    {
        // A note reverses or narrows the clue above it. Dropped, the chunk states the opposite of what
        // the guide says; run together, the label swallows the sentence before it.
        const string html = """
            <html><body>
              <h2 class="country-title">Greece</h2>
              <div class="category-section"><h3 class="category-title">Infrastructure</h3>
                <div class="meta-item" id="infrastructure/a2">
                  <div class="meta-image-wrapper"><img class="meta-image" src="/guides/greece/a2.webp" /></div>
                  <div class="meta-content"><div class="meta-description">The <strong>A2</strong> runs east to west.<aside class="meta-note"><span class="meta-note-label">NOTE:</span> it is the E90 on Google Maps.</aside></div></div>
                </div>
              </div>
            </body></html>
            """;

        var result = await CreateExtractor(html).ExtractAsync(
            new SourceDescriptor("rmrg", "greece", new Uri("https://rmrg.me/greece/")));

        result.IsSuccess.Should().BeTrue();
        var clue = result.Value.Chunks.Should().ContainSingle().Subject;
        clue.Text.Should().Be("The A2 runs east to west.\nNOTE: it is the E90 on Google Maps.");
        clue.SectionPath.Should().Be("Greece > Infrastructure");
        clue.ImageUrl.Should().Be("https://rmrg.me/guides/greece/a2.webp");
    }

    [Fact]
    public async Task List_ReadsGuideSlugsFromTheSitemap()
    {
        const string sitemap = """
            <?xml version="1.0" encoding="UTF-8"?>
            <urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
              <url><loc>https://rmrg.me/</loc></url>
              <url><loc>https://rmrg.me/georgia/</loc></url>
              <url><loc>https://rmrg.me/czech-republic/</loc></url>
              <url><loc>https://rmrg.me/dailychallenge/</loc></url>
            </urlset>
            """;

        var result = await CreateExtractor(sitemap).ListAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.Select(source => source.NaturalKey).Should().Equal("georgia", "czech-republic", "dailychallenge");
        result.Value.Should().OnlyContain(source => source.SourceType == "rmrg");
        result.Value.Select(source => source.Country).Should().Contain("czech republic");
    }

    [Fact]
    public async Task List_KeepsTheTrailingSlash_SoCitationsDoNotPointAtARedirect()
    {
        const string sitemap = """
            <?xml version="1.0" encoding="UTF-8"?>
            <urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
              <url><loc>https://rmrg.me/georgia/</loc></url>
            </urlset>
            """;

        var result = await CreateExtractor(sitemap).ListAsync();

        result.Value.Single().Url.ToString().Should().Be("https://rmrg.me/georgia/");
    }

    [Fact]
    public void CanHandle_AcceptsTheGuideSiteAndNothingElse()
    {
        var extractor = CreateExtractor(string.Empty);

        extractor.CanHandle(new Uri("https://rmrg.me/georgia/")).Should().BeTrue();
        extractor.CanHandle(new Uri("https://www.rmrg.me/poland/")).Should().BeTrue();
        extractor.CanHandle(new Uri("https://www.plonkit.net/tunisia")).Should().BeFalse();
    }

    private static Task<string> ReadFixtureAsync() =>
        File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "AI", "rmrg-georgia.html"));

    private static RmrgSourceExtractor CreateExtractor(string body)
    {
        var httpClient = new HttpClient(new StubHandler(body));

        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(PlonkItSourceExtractor.HttpClientName).Returns(httpClient);

        return new RmrgSourceExtractor(factory, NullLogger<RmrgSourceExtractor>.Instance);
    }

    private sealed class StubHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "text/html"),
                RequestMessage = request
            });
    }
}
