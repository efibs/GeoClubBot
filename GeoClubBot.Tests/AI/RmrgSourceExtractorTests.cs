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
/// Runs against guides captured from the live site, so a change to its markup shows up here rather
/// than as a quietly empty index. No network is involved.
///
/// Georgia is captured twice, before and after the redesign that moved every item from <c>div</c> to
/// <c>article</c> — the change that left every guide reading as empty. The behaviour both captures share
/// runs against both, so neither markup can quietly stop being read again.
/// </summary>
public sealed class RmrgSourceExtractorTests
{
    /// <summary>The guide as served in August 2026, before the redesign.</summary>
    private const string BeforeRedesign = "rmrg-georgia.html";

    /// <summary>The same guide on 23 October 2026, with only the page chrome removed.</summary>
    private const string AfterRedesign = "rmrg-georgia-2026-10.html";

    /// <summary>
    /// The release the redesigned site served its pictures under on capture day,
    /// <c>assets.rmrg.me/r/{release}/…</c>. One value for the whole site: guides last updated in July
    /// and in October carry the same one.
    /// </summary>
    private const string CapturedRelease = "mdb887550ce4d0dc248403200fd3927f8abe67180437bc6118d7d09d3303f556b";

    private static readonly SourceDescriptor Georgia =
        new("rmrg", "georgia", new Uri("https://rmrg.me/georgia/"), Country: "georgia");

    private static readonly SourceDescriptor Bangladesh =
        new("rmrg", "bangladesh", new Uri("https://rmrg.me/bangladesh/"), Country: "bangladesh");

    [Theory]
    [InlineData(BeforeRedesign)]
    [InlineData(AfterRedesign)]
    public async Task Extract_ReadsEveryClueOnThePage(string fixture)
    {
        var result = await CreateExtractor(await ReadFixtureAsync(fixture)).ExtractAsync(Georgia);

        result.IsSuccess.Should().BeTrue();
        result.Value.Title.Should().Be("Georgia");
        result.Value.Chunks.Should().HaveCount(65, "the captured guide lists 65 clues");
    }

    [Theory]
    [InlineData(BeforeRedesign, 2026, 8, 29)]
    [InlineData(AfterRedesign, 2026, 10, 23)]
    public async Task Extract_ReadsTheGuidesOwnUpdateStamp(string fixture, int year, int month, int day)
    {
        // What lets an unchanged guide be skipped without re-embedding it. The markup's own asset
        // version numbers churn independently of the content, so hashing the body would not do.
        var result = await CreateExtractor(await ReadFixtureAsync(fixture)).ExtractAsync(Georgia);

        result.Value.SourceUpdatedAtUtc.Should().NotBeNull();
        result.Value.SourceUpdatedAtUtc!.Value.UtcDateTime.Date.Should().Be(new DateTime(year, month, day));
    }

    [Theory]
    [InlineData(BeforeRedesign)]
    [InlineData(AfterRedesign)]
    public async Task Extract_PairsEveryClueWithThePictureItDescribes(string fixture)
    {
        // The pairing is the whole value of this site: a written question reaches the picture through
        // its caption, which scores far higher than matching the question against the pixels.
        var result = await CreateExtractor(await ReadFixtureAsync(fixture)).ExtractAsync(Georgia);

        result.Value.Chunks.Should().OnlyContain(chunk => chunk.ImageUrl != null);
        result.Value.Chunks.Should().OnlyContain(chunk => chunk.Text.Length > 0);
    }

    [Theory]
    [InlineData(BeforeRedesign,
        "https://rmrg.me/guides/georgia/images-optimized/landscape/l-temperate-rainforest.webp?v=5371")]
    [InlineData(AfterRedesign,
        "https://rmrg.me/guides/georgia/images-optimized/landscape/l-temperate-rainforest.webp?v=20261023")]
    public async Task Extract_TakesThePhotoRatherThanTheAnnotationsDrawnOverIt(string fixture, string photo)
    {
        // An annotated clue stacks a transparent SVG of arrows and circles over a photo. Indexing the
        // overlay would store a picture of arrows pointing at nothing.
        var result = await CreateExtractor(await ReadFixtureAsync(fixture)).ExtractAsync(Georgia);

        var annotated = result.Value.Chunks.Single(chunk => chunk.LocalKey == "landscape/l-temperate-rainforest");

        annotated.ImageUrl.Should().Be(photo);
        result.Value.Chunks.Should().NotContain(chunk => chunk.ImageUrl!.Contains("/vectors"));
    }

    [Theory]
    [InlineData(BeforeRedesign)]
    [InlineData(AfterRedesign)]
    public async Task Extract_PrefersTheOptimisedCopyOverTheFullSizeOriginal(string fixture)
    {
        // Same picture either way, but the originals reach several megabytes and the AI provider
        // fetches these URLs server-side on every embedding call. The redesign keeps the original in
        // data-high-src, which must never win.
        var result = await CreateExtractor(await ReadFixtureAsync(fixture)).ExtractAsync(Georgia);

        result.Value.Chunks.Should().OnlyContain(chunk =>
            chunk.ImageUrl!.StartsWith("https://rmrg.me/guides/georgia/images-optimized/"));
    }

    [Fact]
    public async Task Extract_GivesTheSameChunksWhicheverReleaseThePicturesAreServedFrom()
    {
        // The release token in the asset URL belongs to the whole site, and the image URL is part of
        // each source's change hash. Stored as served, an edit to any one guide would re-embed every
        // guide at the next re-check, and every stored picture would break once the release was retired.
        var html = await ReadFixtureAsync(AfterRedesign);
        var nextRelease = html.Replace(CapturedRelease, "m" + new string('0', 63) + "1", StringComparison.Ordinal);
        nextRelease.Should().NotBe(html, "the capture has to carry the release for this to test anything");

        var captured = await CreateExtractor(html).ExtractAsync(Georgia);
        var released = await CreateExtractor(nextRelease).ExtractAsync(Georgia);

        released.Value.Chunks.Should().Equal(captured.Value.Chunks);
        captured.Value.Chunks.Should().NotContain(chunk => chunk.ImageUrl!.Contains("assets.rmrg.me"));
    }

    [Fact]
    public async Task Extract_LinksEachPictureByThePathTheSiteHasAlwaysServedIt()
    {
        // rmrg.me/guides/{path} is the address the site used before the redesign, and it redirects to
        // the current release. Each picture keeps the path it had then; only the version differs.
        var before = await CreateExtractor(await ReadFixtureAsync(BeforeRedesign)).ExtractAsync(Georgia);
        var after = await CreateExtractor(await ReadFixtureAsync(AfterRedesign)).ExtractAsync(Georgia);

        static string WithoutVersion(string url) => url[..url.IndexOf('?', StringComparison.Ordinal)];

        after.Value.Chunks.ToDictionary(chunk => chunk.LocalKey, chunk => WithoutVersion(chunk.ImageUrl!))
            .Should().BeEquivalentTo(
                before.Value.Chunks.ToDictionary(chunk => chunk.LocalKey, chunk => WithoutVersion(chunk.ImageUrl!)));
    }

    [Fact]
    public async Task Extract_VersionsPicturesByTheGuidesOwnUpdateStamp()
    {
        // The redesign gives a picture no version of its own, so the guide's stamp is what makes a picture
        // replaced under the same file name change its URL, and so its hash. Only this guide's own edit
        // can change it.
        var html = await ReadFixtureAsync(AfterRedesign);
        var edited = html.Replace("23 October 2026", "24 October 2026", StringComparison.Ordinal);
        var unstamped = html.Replace("contributors-updated", "contributors", StringComparison.Ordinal);

        var captured = await CreateExtractor(html).ExtractAsync(Georgia);
        var afterEdit = await CreateExtractor(edited).ExtractAsync(Georgia);
        var withoutStamp = await CreateExtractor(unstamped).ExtractAsync(Georgia);

        captured.Value.Chunks.Should().OnlyContain(chunk => chunk.ImageUrl!.EndsWith("?v=20261023"));
        afterEdit.Value.Chunks.Should().OnlyContain(chunk => chunk.ImageUrl!.EndsWith("?v=20261024"));
        withoutStamp.Value.SourceUpdatedAtUtc.Should().BeNull();
        withoutStamp.Value.Chunks.Should().OnlyContain(chunk =>
            chunk.ImageUrl!.StartsWith("https://rmrg.me/guides/georgia/") && !chunk.ImageUrl.Contains('?'));
    }

    [Fact]
    public async Task Extract_KeepsAnEscapedAssetPathEscapedOnce()
    {
        // The redesign escapes the Greek guide's "linguistic & culture" category in the asset URL itself.
        // Escaped again, the % would become %25 and name a path the site does not have.
        const string html = """
            <html><body>
              <h1 class="country-title">Greece</h1>
              <div class="contributors-updated">Updated 16 July 2026</div>
              <section class="category-section"><h3 class="category-title">Linguistic &amp; Culture</h3>
                <article class="meta-item" id="linguistic-culture/cultural/macedonian-sun">
                  <div class="meta-image-wrapper"><img src="https://assets.rmrg.me/r/mabc/greece/images-optimized/linguistic%20%26%20culture/cultural/macedonian-sun.webp" data-high-src="https://assets.rmrg.me/r/mabc/greece/images/linguistic%20%26%20culture/cultural/macedonian-sun.webp" class="meta-image"></div>
                  <div class="meta-content"><div class="meta-description">The Vergina Sun appears on shop signs.</div></div>
                </article>
              </section>
            </body></html>
            """;

        var result = await CreateExtractor(html).ExtractAsync(
            new SourceDescriptor("rmrg", "greece", new Uri("https://rmrg.me/greece/")));

        result.Value.Chunks.Should().ContainSingle().Which.ImageUrl.Should().Be(
            "https://rmrg.me/guides/greece/images-optimized/linguistic%20%26%20culture/cultural/macedonian-sun.webp?v=20260716");
    }

    [Theory]
    [InlineData(BeforeRedesign)]
    [InlineData(AfterRedesign)]
    public async Task Extract_BuildsSectionPathsFromTheCategoryHeadings(string fixture)
    {
        var result = await CreateExtractor(await ReadFixtureAsync(fixture)).ExtractAsync(Georgia);

        var paths = result.Value.Chunks.Select(chunk => chunk.SectionPath).Distinct().ToList();

        paths.Should().Contain("Georgia > General > Topography", "a category with subcategories");
        paths.Should().Contain("Georgia > Landscape", "a category without them");
        paths.Should().Contain("Georgia > Culture & Language > Islam", "headings arrive HTML-encoded");
        paths.Should().NotContain(path => path.Contains("g-topography"),
            "the redesign's data-title holds the slug on this guide, which is no heading");
    }

    [Theory]
    [InlineData(BeforeRedesign)]
    [InlineData(AfterRedesign)]
    public async Task Extract_UsesTheSitesOwnItemIds_SoKeysAndDeepLinksSurviveReExtraction(string fixture)
    {
        // Point ids derive from these, and the anchor is what makes a citation land on the clue rather
        // than the top of a very long page. A positional key would turn every later chunk into a
        // duplicate the moment the guide gains an item.
        var result = await CreateExtractor(await ReadFixtureAsync(fixture)).ExtractAsync(Georgia);

        result.Value.Chunks.Select(chunk => chunk.LocalKey).Should().OnlyHaveUniqueItems();
        result.Value.Chunks.Should().Contain(chunk => chunk.LocalKey == "architecture/miscellaneous/a-svan-towers");
        result.Value.Chunks.Should().OnlyContain(chunk => chunk.Anchor == chunk.LocalKey);
    }

    [Fact]
    public async Task Extract_KeepsEveryItemsKeyAcrossTheRedesign_SoIndexedPointsUpdateInPlace()
    {
        // The guides indexed before the redesign are re-read under the new markup. Every item that kept
        // its place on the page has to keep its key too, or its point would be swept and re-embedded as
        // a new one — measured across the 13 live guides, every indexed item still on the site does.
        var before = await CreateExtractor(await ReadFixtureAsync(BeforeRedesign)).ExtractAsync(Georgia);
        var after = await CreateExtractor(await ReadFixtureAsync(AfterRedesign)).ExtractAsync(Georgia);

        after.Value.Chunks.Select(chunk => chunk.LocalKey)
            .Should().BeEquivalentTo(before.Value.Chunks.Select(chunk => chunk.LocalKey));
        after.Value.Chunks.Select(chunk => chunk.Anchor)
            .Should().BeEquivalentTo(before.Value.Chunks.Select(chunk => chunk.Anchor));
    }

    [Theory]
    [InlineData(BeforeRedesign)]
    [InlineData(AfterRedesign)]
    public async Task Extract_KeepsTheParagraphBreaksTheAuthorsMarked(string fixture)
    {
        // The breaks are empty spans rather than block elements, so plain InnerText would run three
        // separate observations into one unreadable sentence.
        var result = await CreateExtractor(await ReadFixtureAsync(fixture)).ExtractAsync(Georgia);

        var topography = result.Value.Chunks.Single(chunk => chunk.LocalKey == "general/topography/g-topography");

        topography.Text.Should().StartWith("Georgia has a very interesting and complex topography.");
        topography.Text.Should().Contain("topography.\n\nThere are two large mountain ranges");
        topography.Text.Should().EndWith("using landscape alone.");
    }

    [Theory]
    [InlineData(BeforeRedesign)]
    [InlineData(AfterRedesign)]
    public async Task Extract_KeepsBulletedCluesApart(string fixture)
    {
        // Each bullet is a distinct clue. Run together they read as one sentence that says nothing.
        var result = await CreateExtractor(await ReadFixtureAsync(fixture)).ExtractAsync(Georgia);

        var busStops = result.Value.Chunks.Single(chunk => chunk.LocalKey == "infrastructure/i-bus-stops");

        busStops.Text.Should().Contain("\n- ");
        busStops.Text.Should().NotContain("  ", "collapsed whitespace, so the model sees prose not markup");
    }

    [Fact]
    public async Task Extract_EndsTheSectionPathWithTheItemsOwnTitle()
    {
        // The redesign names most items in data-title, the way the page's own search knows them — "Wing
        // Ridge Ends" — and a topical heading is the largest single influence on retrieval the embedding
        // header has. A title that only repeats the heading above it adds nothing and is left out.
        var result = await CreateExtractor(await ReadFixtureAsync("rmrg-bangladesh.html")).ExtractAsync(Bangladesh);

        var paths = result.Value.Chunks.ToDictionary(chunk => chunk.LocalKey, chunk => chunk.SectionPath);

        paths["agriculture/mango"].Should().Be("Bangladesh > Agriculture > MANGO");
        paths["architecture/clay-houses/clay-houses"].Should().Be("Bangladesh > Architecture > Clay Houses");
        paths["architecture/clay-houses/tall-clay-houses"].Should().Be(
            "Bangladesh > Architecture > Clay Houses > Tall Clay Houses");
        paths["architecture/ridge-ends/wing-ridge-ends"].Should().Be(
            "Bangladesh > Architecture > Ridge Ends > Wing Ridge Ends");
    }

    [Fact]
    public async Task Extract_KeysASubcategorysIntroductionAsBeforeTheRedesign()
    {
        // The prose that opens a subcategory has no slug of its own. Its id used to be the subcategory's
        // path and now ends in "/"; keyed the old way, the point indexed for it is updated rather than
        // swept and embedded afresh. The deep link keeps the id the page carries, so it still lands.
        var result = await CreateExtractor(await ReadFixtureAsync("rmrg-bangladesh.html")).ExtractAsync(Bangladesh);

        var introduction = result.Value.Chunks.Single(chunk => chunk.LocalKey == "architecture/ridge-ends");

        introduction.Anchor.Should().Be("architecture/ridge-ends/");
        introduction.ImageUrl.Should().BeNull("the item is text only");
        introduction.Text.Should().StartWith("In much of Bangladesh you can find metallic ornaments");
        introduction.SectionPath.Should().Be("Bangladesh > Architecture > Ridge Ends", "its title is \"***\"");
    }

    [Fact]
    public async Task Extract_ReadsOnlyTheGuidesItems_NotTheOtherCardsThePageNowMarksAsArticles()
    {
        // The redesign marks the learnable-map and contributor cards up as articles too. Only the class
        // tells a clue apart from them.
        var result = await CreateExtractor(await ReadFixtureAsync("rmrg-bangladesh.html")).ExtractAsync(Bangladesh);

        result.IsSuccess.Should().BeTrue();
        result.Value.Title.Should().Be("Bangladesh");
        result.Value.Chunks.Select(chunk => chunk.LocalKey).Should().Equal(
            "agriculture/mango",
            "architecture/clay-houses/clay-houses",
            "architecture/clay-houses/tall-clay-houses",
            "architecture/ridge-ends",
            "architecture/ridge-ends/wing-ridge-ends");
    }

    [Theory]
    [InlineData("g-topography", "g-topography", "the slug itself")]
    [InlineData("I-bus-stops", "i-bus-stops", "a slug with its one-letter prefix capitalised")]
    [InlineData("l-säntis", "l-santis", "a hyphenated lower-case file name")]
    [InlineData("***", "", "no word at all")]
    [InlineData("Roof Decoration 6", "roof-decoration-6", "the heading above it, numbered")]
    [InlineData("roof decoration", "roof-decoration", "the heading above it in other case")]
    public async Task Extract_LeavesOutAnItemTitleThatIsNoHeading(string title, string slug, string because)
    {
        var html = $"""
            <html><body>
              <h1 class="country-title">Indonesia</h1>
              <section class="category-section"><div class="category-header"><h3 class="category-title">Architecture</h3></div>
                <section class="subcategory-section"><h4 class="subcategory-title">Roof Decoration</h4>
                  <div class="meta-list">
                    <article id="architecture/roof-decoration/x" data-item-slug="{slug}" data-title="{title}" class="meta-item text-only">
                      <div class="meta-content"><div class="meta-description">Carved wooden friezes line the bargeboards.</div></div>
                    </article>
                  </div>
                </section>
              </section>
            </body></html>
            """;

        var result = await CreateExtractor(html).ExtractAsync(
            new SourceDescriptor("rmrg", "indonesia", new Uri("https://rmrg.me/indonesia/")));

        result.Value.Chunks.Should().ContainSingle().Which.SectionPath
            .Should().Be("Indonesia > Architecture > Roof Decoration", because);
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
    public async Task Extract_DecodesEmoji_InHeadingsAndText()
    {
        // A numeric entity for an emoji came out as "&##128204;" under the previous decoder, in the
        // text the model reads and the heading shown under an answer.
        const string html = """
            <html><body>
              <h2 class="country-title">Greece</h2>
              <div class="category-section"><h3 class="category-title">Signs &#128204;</h3>
                <div class="meta-item" id="signs/a2">
                  <div class="meta-image-wrapper"><img class="meta-image" src="/guides/greece/a2.webp" /></div>
                  <div class="meta-content"><div class="meta-description">Yellow signs &#9888;&#65039; mark caf&#233;s on the A2 &#128128;</div></div>
                </div>
              </div>
            </body></html>
            """;

        var result = await CreateExtractor(html).ExtractAsync(
            new SourceDescriptor("rmrg", "greece", new Uri("https://rmrg.me/greece/")));

        var clue = result.Value.Chunks.Should().ContainSingle().Subject;
        clue.Text.Should().Be("Yellow signs ⚠️ mark cafés on the A2 💀");
        clue.SectionPath.Should().Be("Greece > Signs 📌");
    }

    [Fact]
    public async Task Extract_KeepsAClueWhosePictureIsAVector_WithoutThePicture()
    {
        // Not every item here is a photograph: symbols and flags are drawn, and the site serves those
        // as .svgz from every attribute rather than only as an overlay. The AI provider answers 415 for
        // them and fails the whole request they were batched into, so the prose is indexed on its own.
        const string html = """
            <html><body>
              <h2 class="country-title">Indonesia</h2>
              <div class="category-section"><h3 class="category-title">Culture &amp; Language</h3>
                <div class="meta-item" id="culture-language/religion/hinduism-2">
                  <div class="meta-image-wrapper"><img class="meta-image" src="/guides/indonesia/vectors/religion/hinduism-2.svg?v=8" data-optimized-src="/guides/indonesia/vectors-optimized/religion/hinduism-2.svgz?v=8" /></div>
                  <div class="meta-content"><div class="meta-description">Balinese Hindu shrines are common in the east.</div></div>
                </div>
              </div>
            </body></html>
            """;

        var result = await CreateExtractor(html).ExtractAsync(
            new SourceDescriptor("rmrg", "indonesia", new Uri("https://rmrg.me/indonesia/")));

        var clue = result.Value.Chunks.Should().ContainSingle().Subject;
        clue.Text.Should().Be("Balinese Hindu shrines are common in the east.");
        clue.ImageUrl.Should().BeNull();
    }

    [Fact]
    public async Task Extract_DecodesImagePaths_SoACategoryWithAnAmpersandIsNotA404()
    {
        // Measured against the live site: the Greek guide files four of its pictures under a
        // "linguistic & culture" category, and the attribute carries that as "&amp;". Sent on
        // unchanged it names a path the host does not have, so every image under it came back 404 —
        // for the embedding provider and for Discord alike. The space has to survive as %20 too,
        // which is why the escaped form of the URL is what gets stored.
        const string html = """
            <html><body>
              <h2 class="country-title">Greece</h2>
              <div class="category-section"><h3 class="category-title">Linguistic &amp; Culture</h3>
                <div class="meta-item" id="linguistic-culture/cultural/macedonian-sun">
                  <div class="meta-image-wrapper"><img class="meta-image" data-optimized-src="/guides/greece/images-optimized/linguistic &amp; culture/cultural/macedonian-sun.webp?v=9" /></div>
                  <div class="meta-content"><div class="meta-description">The Vergina Sun appears on shop signs.</div></div>
                </div>
              </div>
            </body></html>
            """;

        var result = await CreateExtractor(html).ExtractAsync(
            new SourceDescriptor("rmrg", "greece", new Uri("https://rmrg.me/greece/")));

        result.Value.Chunks.Should().ContainSingle().Which.ImageUrl.Should().Be(
            "https://rmrg.me/guides/greece/images-optimized/linguistic%20&%20culture/cultural/macedonian-sun.webp?v=9");
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

    private static Task<string> ReadFixtureAsync(string name) =>
        File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "AI", name));

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
