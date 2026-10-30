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
/// Runs against a page captured from the live site, so a change to its embedded payload shape shows
/// up here rather than as a quietly empty index. No network is involved.
/// </summary>
public sealed class PlonkItSourceExtractorTests
{
    private static readonly SourceDescriptor Tunisia =
        new("plonkit", "tunisia", new Uri("https://www.plonkit.net/tunisia"), Country: "tunisia");

    private static readonly SourceDescriptor Mongolia =
        new("plonkit", "mongolia", new Uri("https://www.plonkit.net/mongolia"), Country: "mongolia");

    private static readonly SourceDescriptor UnitedStates =
        new("plonkit", "united-states", new Uri("https://www.plonkit.net/united-states"), Country: "united states");

    private static readonly SourceDescriptor BeginnersGuide =
        new("plonkit", "beginners-guide", new Uri("https://www.plonkit.net/beginners-guide"));

    [Fact]
    public async Task Extract_ReadsEveryGuideItem_FromTheEmbeddedPayload()
    {
        var result = await CreateExtractor(await ReadFixtureAsync()).ExtractAsync(Tunisia);

        result.IsSuccess.Should().BeTrue();
        result.Value.Title.Should().Be("Tunisia");
        result.Value.Chunks.Should().HaveCount(30, "the captured guide has 29 tips and one standalone image");
    }

    [Fact]
    public async Task Extract_ReadsTheUpstreamChangeMarker()
    {
        // updatedAt is what lets an unchanged guide be skipped without downloading and hashing it.
        var result = await CreateExtractor(await ReadFixtureAsync()).ExtractAsync(Tunisia);

        result.Value.SourceUpdatedAtUtc.Should().NotBeNull();
        result.Value.SourceUpdatedAtUtc!.Value.UtcDateTime.Date.Should().Be(new DateTime(2026, 8, 9));
    }

    [Fact]
    public async Task Extract_ReducesLinksToTheirText()
    {
        // The guide links example locations and species pages. The targets say nothing a question
        // would, and measured against the embedding model they lowered each linked tip's similarity
        // to the question it answers by 0.04 to 0.06 — from about 8th to 39th for one of them.
        var result = await CreateExtractor(await ReadFixtureAsync()).ExtractAsync(Tunisia);

        var texts = result.Value.Chunks.Select(chunk => chunk.Text).ToList();

        texts.Should().NotContain(text => text.Contains("](http", StringComparison.Ordinal));
        texts.Should().NotContain(text => text.Contains("goo.gl", StringComparison.Ordinal));
        texts.Should().Contain(text => text.Contains("yellow middle lines", StringComparison.Ordinal),
            "the link's text is kept; only its target goes");
    }

    [Fact]
    public async Task Extract_KeepsImagesWithTheProseThatDescribesThem()
    {
        // The pairing is the point: a written question reaches the picture through its caption, which
        // scores far higher than matching the question against the pixels.
        var result = await CreateExtractor(await ReadFixtureAsync()).ExtractAsync(Tunisia);

        var licencePlate = result.Value.Chunks.Single(chunk => chunk.Text.Contains("licence plates"));

        licencePlate.ImageUrl.Should().Be("https://www.plonkit.net/images/tunisia/Tunisia_License_Plate.png");
        licencePlate.Text.Should().Contain("black with white text");
    }

    [Fact]
    public async Task Extract_CaptionsTheSummaryInfographicTheGuideOpensWith()
    {
        // A centeredImage carries no caption of its own, and without text it would have no text vector
        // at all. It used to inherit only "Tunisia — Identifying Tunisia", which repeats the header every
        // chunk of the guide carries; the picture is the guide's summary, so that is what it is called.
        var result = await CreateExtractor(await ReadFixtureAsync()).ExtractAsync(Tunisia);

        var summary = result.Value.Chunks.Single(chunk => chunk.LocalKey == "0/m1jr");

        summary.ImageUrl.Should().NotBeNullOrEmpty();
        summary.Text.Should().StartWith("Tunisia summary infographic");
        summary.SectionPath.Should().Be("Tunisia > Identifying Tunisia");
    }

    [Fact]
    public async Task Extract_CaptionsAStandaloneImageUnderAHeadingWithItsHeadings()
    {
        // Only the picture a guide opens with is its summary. One further in has nothing but its place
        // in the guide to be found by, and the headings are the topical part of that.
        var result = await CreateExtractor(await ReadFixtureAsync("plonkit-beginners-guide.html"))
            .ExtractAsync(BeginnersGuide);

        var sunDirection = result.Value.Chunks.Single(chunk => chunk.LocalKey == "2/bMdz");

        sunDirection.ImageUrl.Should().Be("https://www.plonkit.net/images/beginners-guide/whattolook_sundirection.png");
        sunDirection.Text.Should().Be(
            "Beginner's Guide to Geoguessr — What to look for in-game — General clues — Sun direction");
    }

    [Fact]
    public async Task Extract_PutsTheGuidesTopicalHeadingsIntoTheSectionPath()
    {
        // Mongolia's tent tips sit under "Car metas" and were indexed without it, so a question about
        // the car's tent straps had only the tip's own words to match. A source's own topical heading is
        // the largest single influence on retrieval the embedding header has.
        var result = await CreateExtractor(await ReadFixtureAsync("plonkit-mongolia.html")).ExtractAsync(Mongolia);

        var paths = result.Value.Chunks.ToDictionary(chunk => chunk.LocalKey, chunk => chunk.SectionPath);

        paths["0/000d"].Should().Be("Mongolia > Identifying Mongolia", "no heading precedes it");
        paths["1/nIEA"].Should().Be("Mongolia > Region-specific clues > Major regionguessing tips");
        paths["1/62RZ"].Should().Be("Mongolia > Region-specific clues > Car metas");
        paths["1/gdTt"].Should().Be("Mongolia > Region-specific clues > Car metas");
        paths["2/lmEn"].Should().Be("Mongolia > Spotlight > Towns", "a heading never carries into the next step");
    }

    [Fact]
    public async Task Extract_NestsTheOtherHeadingKindBeneathTheOneAStepOpensWith()
    {
        // Guides use dividers and subsections both ways round: the United States puts subsections under
        // a divider, the beginners' guide a divider under a subsection.
        var unitedStates = await CreateExtractor(await ReadFixtureAsync("plonkit-united-states.html"))
            .ExtractAsync(UnitedStates);
        var beginners = await CreateExtractor(await ReadFixtureAsync("plonkit-beginners-guide.html"))
            .ExtractAsync(BeginnersGuide);

        var paths = unitedStates.Value.Chunks.Concat(beginners.Value.Chunks)
            .ToDictionary(chunk => chunk.LocalKey, chunk => chunk.SectionPath);

        const string regional = "United States of America > Regional and state-specific clues";
        paths["1/a1wL"].Should().Be(regional);
        paths["1/avXB"].Should().Be($"{regional} > Infrastructure");
        paths["1/rwXR"].Should().Be($"{regional} > Infrastructure > Road Features");
        paths["1/tVFe"].Should().Be($"{regional} > Infrastructure > Poles");
        paths["1/71B8"].Should().Be($"{regional} > Architecture", "a new outer heading ends the inner one");

        paths["1/7pgH"].Should().Be(
            "Beginner's Guide to Geoguessr > Gamemodes > Cooperative games > Making and playing in a Party");
        paths["1/synQ"].Should().Be("Beginner's Guide to Geoguessr > Gamemodes > Competitive games");
    }

    [Fact]
    public async Task Extract_EndsAHeadingAtAnUntitledOne()
    {
        // The United States follows its store-chain overview with an untitled subsection: the heading
        // in force is the divider above it again, not a blank segment.
        var result = await CreateExtractor(await ReadFixtureAsync("plonkit-united-states.html"))
            .ExtractAsync(UnitedStates);

        var paths = result.Value.Chunks.ToDictionary(chunk => chunk.LocalKey, chunk => chunk.SectionPath);

        paths["2/CZYl"].Should().Be("United States of America > Spotlight > Store Chains");
        paths["2/qWZp"].Should().Be("United States of America > Spotlight > Store Chains");
    }

    [Fact]
    public async Task Extract_DropsTheStepNumberAHeadingOpensWith()
    {
        // "Step 1.1 - US-specific": the number is position, not subject, and filler in the embedding
        // header costs almost everything a topical heading gains.
        var result = await CreateExtractor(await ReadFixtureAsync("plonkit-united-states.html"))
            .ExtractAsync(UnitedStates);

        var paths = result.Value.Chunks.ToDictionary(chunk => chunk.LocalKey, chunk => chunk.SectionPath);

        paths["0/a9Az"].Should().Be("United States of America > Identifying the United States > US-specific");
        paths["0/IXin"].Should().Be("United States of America > Identifying the United States > US & Canada");
    }

    [Fact]
    public async Task Extract_IndexesTheNotesBetweenTips_AsText()
    {
        // Mongolia's note explains what the colours on its car-meta maps mean. Without it the model is
        // shown a map it cannot read.
        var result = await CreateExtractor(await ReadFixtureAsync("plonkit-mongolia.html")).ExtractAsync(Mongolia);

        var notes = result.Value.Chunks.Single(chunk => chunk.LocalKey == "1/C7TF");

        notes.ImageUrl.Should().BeNull();
        notes.Anchor.Should().Be("C7TF");
        notes.SectionPath.Should().Be("Mongolia > Region-specific clues > Car metas");
        notes.Text.Should().Contain("without relying on cars.\n\nRoads highlighted in red will **only** have that car",
            "each paragraph of the note stays a paragraph");
    }

    [Fact]
    public async Task Extract_IndexesCentredAndPlainText_WhicheverShapeItsTextHas()
    {
        // Centred text carries one string where text and notes items carry a list of paragraphs.
        var unitedStates = await CreateExtractor(await ReadFixtureAsync("plonkit-united-states.html"))
            .ExtractAsync(UnitedStates);
        var beginners = await CreateExtractor(await ReadFixtureAsync("plonkit-beginners-guide.html"))
            .ExtractAsync(BeginnersGuide);

        var centred = unitedStates.Value.Chunks.Single(chunk => chunk.LocalKey == "0/I0ly");
        centred.Text.Should().StartWith("The following information applies to both the US and Canada.");
        centred.ImageUrl.Should().BeNull();
        centred.SectionPath.Should().Be("United States of America > Identifying the United States > US & Canada");

        var plain = beginners.Value.Chunks.Single(chunk => chunk.LocalKey == "1/Bkwr");
        plain.Text.Should().StartWith("Geoguessr players often talk about three different main gamemodes.\n\n- **Moving games**");
        plain.SectionPath.Should().Be("Beginner's Guide to Geoguessr > Gamemodes > Single player games");
    }

    [Fact]
    public async Task Extract_IndexesACaptionedPictureWithItsOwnCaption()
    {
        // The caption sits on the item itself rather than under "data" as a tip's text does, which is
        // why these pictures were never read.
        var result = await CreateExtractor(await ReadFixtureAsync("plonkit-beginners-guide.html"))
            .ExtractAsync(BeginnersGuide);

        var hud = result.Value.Chunks.Single(chunk => chunk.LocalKey == "0/n5tt");

        hud.ImageUrl.Should().Be("https://www.plonkit.net/images/beginners-guide/Geoguessr_HUD.png");
        hud.Text.Should().Be("The interface of a classic game of Geoguessr with moving settings.");
        hud.SectionPath.Should().Be("Beginner's Guide to Geoguessr > Game mechanics > In game");
        hud.Anchor.Should().Be("n5tt");
    }

    [Fact]
    public async Task Extract_ReadsContentItemsOnly_NotHeadingsOrNavigation()
    {
        // Headings label the items after them rather than being content of their own; the navigation
        // buttons and the video carry nothing an excerpt or a picture could show.
        var result = await CreateExtractor(await ReadFixtureAsync("plonkit-beginners-guide.html"))
            .ExtractAsync(BeginnersGuide);

        result.Value.Chunks.Select(chunk => chunk.LocalKey).Should().Equal(
            "0/LFAW", "0/n5tt", "0/z8YP", "1/Bkh2", "1/Bkwr", "1/7pgH", "1/synQ", "2/ATgu", "2/bMdz");
    }

    [Fact]
    public async Task Extract_KeepsTheKeysOfItemsAlreadyIndexed_AndGivesNewKindsKeysOfTheSameShape()
    {
        // Reading more of a guide must not move what is already there: a tip's point is updated in place
        // only while its key stays "{step}/{id}", and an item read for the first time takes a key of the
        // same shape rather than shifting anything after it.
        var result = await CreateExtractor(await ReadFixtureAsync("plonkit-mongolia.html")).ExtractAsync(Mongolia);

        result.Value.Chunks.Select(chunk => chunk.LocalKey).Should().Equal(
            "0/UFlm", "0/000d", "1/nIEA", "1/NW0E", "1/C7TF", "1/WGaz", "1/62RZ", "1/gdTt", "2/lmEn");
        result.Value.Chunks.Should().OnlyContain(chunk => chunk.LocalKey.EndsWith("/" + chunk.Anchor, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Extract_UsesTheSitesOwnItemIds_SoKeysSurviveReExtraction()
    {
        // Point ids derive from these. A positional key would shift whenever the guide gains an item,
        // turning every later chunk into a duplicate on the next ingest.
        var result = await CreateExtractor(await ReadFixtureAsync()).ExtractAsync(Tunisia);

        result.Value.Chunks.Select(chunk => chunk.LocalKey).Should().OnlyHaveUniqueItems();
        result.Value.Chunks.Should().Contain(chunk => chunk.LocalKey == "0/1chu");
    }

    [Fact]
    public async Task Extract_BuildsSectionPathsFromTheGuideStructure()
    {
        var result = await CreateExtractor(await ReadFixtureAsync()).ExtractAsync(Tunisia);

        result.Value.Chunks.Select(chunk => chunk.SectionPath).Distinct().Should().BeEquivalentTo(
            "Tunisia > Identifying Tunisia",
            "Tunisia > Regional and governorate-specific clues",
            "Tunisia > Spotlight");
    }

    [Fact]
    public async Task Extract_ResolvesRelativeImagePathsToAbsoluteUrls()
    {
        // The provider fetches these server-side, so a site-relative path would simply fail.
        var result = await CreateExtractor(await ReadFixtureAsync()).ExtractAsync(Tunisia);

        result.Value.Chunks
            .Where(chunk => chunk.ImageUrl is not null)
            .Should().OnlyContain(chunk => chunk.ImageUrl!.StartsWith("https://www.plonkit.net/images/"));
    }

    [Fact]
    public async Task Extract_ReportsANonGuidePageAsSkippable()
    {
        // The sitemap mixes guides with leaderboards and rules pages. These are reported as a
        // validation failure so the caller records them as skipped rather than retrying nightly.
        var extractor = CreateExtractor("<html><body>Leaderboard</body></html>");

        var result = await extractor.ExtractAsync(
            new SourceDescriptor("plonkit", "leaderboard", new Uri("https://www.plonkit.net/leaderboard")));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("ai.not_a_guide_page");
        result.Error.Type.Should().Be(ErrorType.Validation);
    }

    [Fact]
    public async Task Extract_ReportsTheGuideIndexAsSkippable_NotAsAParseFailure()
    {
        // /guide carries a payload like a guide page does, but its "data" is the array of all 140
        // guides rather than one guide object. Parsing it threw, which recorded the page as failed —
        // so it was retried, and warned about, on every nightly run. It is simply not a guide.
        const string html = """
            <html><script>window.__d={"success":true,"data":[{"slug":"tunisia"},{"slug":"greece"}]}</script></html>
            """;

        var result = await CreateExtractor(html).ExtractAsync(
            new SourceDescriptor("plonkit", "guide", new Uri("https://www.plonkit.net/guide")));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("ai.not_a_guide_page");
        result.Error.Type.Should().Be(ErrorType.Validation);
    }

    [Fact]
    public async Task Extract_SurvivesBracesInsideGuideProse()
    {
        // The payload is located by brace-matching, which must respect string state — a brace written
        // inside a guide sentence would otherwise end the object early and corrupt the parse.
        const string html = """
            <html><script>window.__d={"success":true,"data":{"public":{"title":"Braces {ahoy}","slug":"x",
            "updatedAt":"2026-01-01T00:00:00.000Z","steps":[{"kind":"tip","title":"S","items":[
            {"kind":"tip","id":"a1","data":{"text":["A brace } in prose, and a quote \" too."]}}]}]}}}</script></html>
            """;

        var result = await CreateExtractor(html).ExtractAsync(Tunisia);

        result.IsSuccess.Should().BeTrue();
        result.Value.Title.Should().Be("Braces {ahoy}");
        result.Value.Chunks.Should().ContainSingle().Which.Text.Should().Contain("brace }");
    }

    [Fact]
    public async Task List_ReadsCountrySlugsFromTheSitemap()
    {
        const string sitemap = """
            <?xml version="1.0" encoding="UTF-8"?>
            <urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
              <url><loc>https://www.plonkit.net/</loc></url>
              <url><loc>https://www.plonkit.net/tunisia</loc></url>
              <url><loc>https://www.plonkit.net/kenya</loc></url>
              <url><loc>https://www.plonkit.net/leaderboard</loc></url>
            </urlset>
            """;

        var result = await CreateExtractor(sitemap).ListAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.Select(source => source.NaturalKey).Should().Equal("tunisia", "kenya", "leaderboard");
        result.Value.Should().OnlyContain(source => source.SourceType == "plonkit");
    }

    [Fact]
    public void CanHandle_AcceptsPlonkItHostsIncludingRegionalMirrors()
    {
        var extractor = CreateExtractor(string.Empty);

        extractor.CanHandle(new Uri("https://www.plonkit.net/tunisia")).Should().BeTrue();
        extractor.CanHandle(new Uri("https://de.plonkit.net/images/x.png")).Should().BeTrue();
        extractor.CanHandle(new Uri("https://imgur.com/a/abc")).Should().BeFalse();
    }

    /// <summary>
    /// Tunisia is captured whole. The others are live pages trimmed to the items these tests read, with
    /// their step indices kept, so every key matches the live guide's.
    /// </summary>
    private static Task<string> ReadFixtureAsync(string name = "plonkit-tunisia.html") =>
        File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "AI", name));

    private static PlonkItSourceExtractor CreateExtractor(string body)
    {
        var httpClient = new HttpClient(new StubHandler(body));

        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(PlonkItSourceExtractor.HttpClientName).Returns(httpClient);

        return new PlonkItSourceExtractor(factory, NullLogger<PlonkItSourceExtractor>.Instance);
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
