using FluentAssertions;
using UseCases.UseCases.AI.Ingestion;
using Xunit;

namespace GeoClubBot.Tests.AI;

/// <summary>
/// The guard that keeps a picture nothing can read out of an embedding request. A refusal fails the
/// whole batch it travelled in, so the cost of letting one through is paid by every image beside it
/// and by the requests spent narrowing the batch down afterwards.
/// </summary>
public sealed class ImageFormatsTests
{
    [Theory]
    [InlineData("https://i.imgur.com/a.png")]
    [InlineData("https://rmrg.me/guides/georgia/images-optimized/landscape/l-rainforest.webp?v=5371")]
    [InlineData("https://www.plonkit.net/photo.JPEG")]
    [InlineData("https://example.net/image")]
    [InlineData("https://example.net/render?id=42")]
    [InlineData("data:image/png;base64,iVBORw0KGgo=")]
    public void CanEmbed_AcceptsAnythingNotKnownToBeUnusable(string imageUrl) =>
        // Extensionless URLs included: plenty of hosts serve ordinary JPEGs from them, and demanding a
        // known-good extension would cost far more pictures than the broken ones save.
        ImageFormats.CanEmbed(imageUrl).Should().BeTrue();

    [Theory]
    [InlineData("https://rmrg.me/guides/indonesia/vectors-optimized/religion/hinduism-2.svgz?v=5370")]
    [InlineData("https://rmrg.me/guides/indonesia/vectors/religion/hinduism-2.svg")]
    [InlineData("https://flagcdn.com/ge.SVG")]
    [InlineData("https://example.net/scan.pdf")]
    [InlineData("https://example.net/photo.avif")]
    [InlineData("data:image/svg+xml;base64,PHN2Zz4=")]
    public void CanEmbed_RefusesFormatsTheProviderCannotRead(string imageUrl) =>
        // The cache-busting query is the point of the first case: it leaves the URL ending in no
        // extension at all until the query is taken off, which is how these reached the provider.
        ImageFormats.CanEmbed(imageUrl).Should().BeFalse();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void CanEmbed_RefusesAnAbsentUrl(string? imageUrl) =>
        ImageFormats.CanEmbed(imageUrl).Should().BeFalse();

    [Fact]
    public void IsSupportedPath_RecognisesOnlyTheRasterFormats() =>
        // What decides whether a bare link is catalogued as an image source at all.
        new[] { "/a.png", "/a.jpg", "/a.jpeg", "/a.gif", "/a.webp", "/a.svg", "/a", "/a.pdf" }
            .Where(ImageFormats.IsSupportedPath)
            .Should().Equal("/a.png", "/a.jpg", "/a.jpeg", "/a.gif", "/a.webp");
}
