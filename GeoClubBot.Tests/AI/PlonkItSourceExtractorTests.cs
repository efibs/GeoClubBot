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
    public async Task Extract_GivesAStandaloneImageItsSectionHeadingAsText()
    {
        // A centeredImage carries no caption of its own. Without inherited text it would have no text
        // vector at all and be unreachable by a written question.
        var result = await CreateExtractor(await ReadFixtureAsync()).ExtractAsync(Tunisia);

        var standalone = result.Value.Chunks.Single(chunk => chunk.LocalKey.EndsWith("/m1jr", StringComparison.Ordinal));

        standalone.ImageUrl.Should().NotBeNullOrEmpty();
        standalone.Text.Should().Contain("Tunisia").And.Contain("Identifying");
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

    private static Task<string> ReadFixtureAsync() =>
        File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "AI", "plonkit-tunisia.html"));

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
