using FluentAssertions;
using UseCases.UseCases.AI.Ingestion;
using Xunit;

namespace GeoClubBot.Tests.AI;

/// <summary>
/// URLs here are real entries from the guide library, including the awkward shapes: imgur's
/// slugged album links, Google document ids with hyphens and underscores, and the sites that have
/// no extractor at all.
/// </summary>
public sealed class SourceLinkClassifierTests
{
    [Theory]
    [InlineData("https://www.plonkit.net/tunisia", "plonkit", "tunisia")]
    [InlineData("https://www.plonkit.net/botswana", "plonkit", "botswana")]
    [InlineData("https://rmrg.me/georgia/", "rmrg", "georgia")]
    [InlineData("https://rmrg.me/czech-republic/", "rmrg", "czech-republic")]
    [InlineData("https://docs.google.com/document/d/1rw0j0Q5z_vLE1K5bYLKXDpxW8wXLJQl7vIq8YqVBcGM/edit", "gdoc", "1rw0j0Q5z_vLE1K5bYLKXDpxW8wXLJQl7vIq8YqVBcGM")]
    [InlineData("https://docs.google.com/presentation/d/1abcDEF-ghi_JKL/edit#slide=id.p", "gslides", "1abcDEF-ghi_JKL")]
    [InlineData("https://docs.google.com/spreadsheets/d/1JdyJNoOkksLAdGnJ_scA17K1pHV3THHERY/edit", "gsheet", "1JdyJNoOkksLAdGnJ_scA17K1pHV3THHERY")]
    [InlineData("https://imgur.com/a/e3RTN2O", "imgur", "e3RTN2O")]
    [InlineData("https://imgur.com/gallery/P0Lt4KI", "imgur", "P0Lt4KI")]
    [InlineData("https://i.imgur.com/zllc1ua.png", "image", "https://i.imgur.com/zllc1ua.png")]
    public void Classify_RecognisesTheSupportedFamilies(string url, string expectedType, string expectedKey)
    {
        var classified = SourceLinkClassifier.Classify(new Uri(url));

        classified.IsSupported.Should().BeTrue();
        classified.SourceType.Should().Be(expectedType);
        classified.NaturalKey.Should().Be(expectedKey);
    }

    [Fact]
    public void Classify_ExtractsTheIdFromASluggedImgurAlbum()
    {
        // imgur prefixes album links with a human-readable slug; only the trailing id addresses the
        // album, and treating the whole path as the key would index the same album twice.
        var classified = SourceLinkClassifier.Classify(
            new Uri("https://imgur.com/a/region-guessing-kairouan-tunisia-street-signs-cflkkR1"));

        classified.SourceType.Should().Be("imgur");
        classified.NaturalKey.Should().Be("cflkkR1");
    }

    [Theory]
    [InlineData("https://discord.com/channels/854419081813164042/855528394229415966/1247")]
    [InlineData("https://cdn.discordapp.com/attachments/1037713686581755985/112680.png")]
    public void Classify_RefusesDiscordLinks(string url)
    {
        // Recorded rather than dropped: 150 of the library's entries are Discord links, and a
        // silently missing sixth of a library looks like full coverage.
        var classified = SourceLinkClassifier.Classify(new Uri(url));

        classified.IsSupported.Should().BeFalse();
        classified.UnsupportedReason.Should().Contain("authenticated");
    }

    [Theory]
    [InlineData("https://www.youtube.com/playlist?list=PLyMIkeJHw2yn")]
    [InlineData("https://youtu.be/abc123")]
    public void Classify_RefusesVideoGuides(string url)
    {
        var classified = SourceLinkClassifier.Classify(new Uri(url));

        classified.IsSupported.Should().BeFalse();
        classified.UnsupportedReason.Should().Contain("speech");
    }

    [Theory]
    [InlineData("https://super-duper.fr/maps/botswana_en.php")]
    [InlineData("https://botswanadocument.tumblr.com/")]
    [InlineData("https://drive.google.com/file/d/1FFK85dJQlCKQYkzDeX8FA3kAyuJdI55Z/view")]
    [InlineData("https://docs.google.com/forms/d/1B8uyi-ahswL20RhtLA6fXsld8FEKUUKArvJ7gi/edit")]
    [InlineData("https://docs.google.com/drawings/d/11OO740LO07dcAcUk-lQtZ4gPhKbbUtCG/edit")]
    public void Classify_RecordsOneOffSitesAsUnsupported(string url)
    {
        // Every link in a library is accounted for, so coverage can be reported honestly.
        var classified = SourceLinkClassifier.Classify(new Uri(url));

        classified.IsSupported.Should().BeFalse();
        classified.SourceType.Should().Be("unsupported");
        classified.UnsupportedReason.Should().Contain("No extractor supports");
    }

    [Fact]
    public void Classify_RefusesAPlonkItLinkThatIsNotACountryPage()
    {
        SourceLinkClassifier.Classify(new Uri("https://www.plonkit.net/images/tunisia/x.png"))
            .SourceType.Should().Be("image", "a bare image is still indexable even on that host");

        SourceLinkClassifier.Classify(new Uri("https://www.plonkit.net/a/b/c"))
            .IsSupported.Should().BeFalse();
    }

    [Fact]
    public void Classify_RefusesAnRmrgLinkThatIsNotACountryPage()
    {
        SourceLinkClassifier.Classify(new Uri("https://rmrg.me/guides/georgia/images/general/x.webp"))
            .SourceType.Should().Be("image", "a bare image is still indexable even on that host");

        SourceLinkClassifier.Classify(new Uri("https://rmrg.me/guides/georgia/a/b"))
            .IsSupported.Should().BeFalse();
    }
}
