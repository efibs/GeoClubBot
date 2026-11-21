using FluentAssertions;
using UseCases.UseCases.AI.Review;
using UseCases.UseCases.AI.StreetView;
using Xunit;

namespace GeoClubBot.Tests.AI;

/// <summary>Reading Google Maps Street View links and the links <c>/ai review</c> takes (#480).</summary>
public sealed class StreetViewLinkTests
{
    /// <summary>The issue's own example, as the short link led to it on 2026-11-21.</summary>
    private const string IssueLink =
        "https://www.google.com/maps/@34.0519954,-99.9644448,535a,90y,51.57h,92.16t/data=!3m7!1e1!3m5!1s3kZJIREjaW8A1P039qpRvA!2e0!6shttps:%2F%2Fstreetviewpixels-pa.clients6.google.com%2Fv1%2Fthumbnail%3Fcb_client%3Dmaps_sv.tactile%26w%3D900%26h%3D600%26pitch%3D-2.160179290400393%26panoid%3D3kZJIREjaW8A1P039qpRvA%26yaw%3D51.574500307776034!7i16384!8i8192?entry=ttu&g_ep=EgoyMDI2MTExMC4wIKXMDSoASAFQAw%3D%3D";

    [Fact]
    public void Read_TakesThePlaceThePanoramaAndTheView_FromALongLink()
    {
        var spot = StreetViewLink.Read(IssueLink)!;

        spot.PanoId.Should().Be("3kZJIREjaW8A1P039qpRvA");
        spot.Latitude.Should().Be(34.0519954);
        spot.Longitude.Should().Be(-99.9644448);
        spot.Heading.Should().BeApproximately(51.57, 1e-9);
        spot.Pitch.Should().BeApproximately(2.16, 1e-9, "the tilt is 90 at the horizon");
        spot.FieldOfView.Should().Be(90);
    }

    [Theory]
    [InlineData("https://www.google.com/maps/@?api=1&map_action=pano&pano=tph3yE31gcf1MCnaufqVUg&viewpoint=21.8,106.7&heading=225&pitch=-10&fov=80")]
    [InlineData("https://www.google.de/maps/@21.8,106.7,3a,80y,225h,80t/data=!3m6!1e1!3m4!1stph3yE31gcf1MCnaufqVUg!2e0!7i16384!8i8192")]
    public void Read_KnowsTheDocumentedFormAndOtherCountriesGoogle(string link)
    {
        var spot = StreetViewLink.Read(link)!;

        spot.Should().BeEquivalentTo(new { PanoId = "tph3yE31gcf1MCnaufqVUg", Latitude = 21.8, Longitude = 106.7, Heading = 225.0, Pitch = -10.0, FieldOfView = 80.0 });
    }

    [Fact]
    public void Read_FindsNoPanorama_InAMapLink_OrAPhotoSphere()
    {
        StreetViewLink.Read("https://www.google.com/maps/@34.05,-99.96,15z")!.PanoId.Should().BeNull();
        StreetViewLink.Read("https://www.google.com/maps/@34.05,-99.96,3a,75y,90t/data=!3m8!1e1!3m6!1sAF1QipNxyzxyzxyzxyzxyzxyz!2e10!3e11")!.PanoId
            .Should().BeNull("only Google's own coverage (!2e0) is served by the thumbnail endpoint");
        StreetViewLink.Read("https://example.com/maps/@34.05,-99.96,15z").Should().BeNull();
        StreetViewLink.Read("https://maps.app.goo.gl/dfn7LaomB9WMUtKu5").Should().BeNull("a short link says nothing until it is followed");
    }

    [Fact]
    public void ToMapsUrl_OpensStreetViewWhereTheSpotLooks()
    {
        new StreetViewSpot("3kZJIREjaW8A1P039qpRvA", 34.0519954, -99.9644448, 51.57, 2.16, 90).MapsUrl.Should().Be(
            "https://www.google.com/maps/@?api=1&map_action=pano&pano=3kZJIREjaW8A1P039qpRvA&viewpoint=34.0519954,-99.9644448&heading=51.57&pitch=2.16&fov=90");
        StreetViewLink.Read(new StreetViewSpot("3kZJIREjaW8A1P039qpRvA", 34.05, -99.96, 51.57, 2.16, 90).MapsUrl)
            .Should().Be(new StreetViewSpot("3kZJIREjaW8A1P039qpRvA", 34.05, -99.96, 51.57, 2.16, 90), "what it writes, it reads back");
    }

    [Theory]
    [InlineData("what gives this away? https://maps.app.goo.gl/dfn7LaomB9WMUtKu5", "https://maps.app.goo.gl/dfn7LaomB9WMUtKu5", true)]
    [InlineData("(<https://goo.gl/maps/AbC123>)", "https://goo.gl/maps/AbC123", true)]
    [InlineData("here: https://www.google.com/maps/@34.05,-99.96,15z.", "https://www.google.com/maps/@34.05,-99.96,15z", false)]
    public void Find_PicksTheLinkOutOfAMessage(string text, string expected, bool isShort)
    {
        var link = StreetViewLink.Find(text);

        link.Should().Be(expected);
        StreetViewLink.IsShortLink(link).Should().Be(isShort);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("no link here")]
    [InlineData("https://www.geoguessr.com/game/dhmguJ9rQTqI6cmA")]
    [InlineData("https://maps.apple.com/?ll=34.05,-99.96")]
    public void Find_FindsNothing_WithoutAGoogleMapsLink(string? text) =>
        StreetViewLink.Find(text).Should().BeNull();

    [Theory]
    [InlineData("https://www.geoguessr.com/challenge/AbCdEf123456", ReviewLinkKind.Challenge, "AbCdEf123456")]
    [InlineData("<https://www.geoguessr.com/challenge/AbCdEf123456>", ReviewLinkKind.Challenge, "AbCdEf123456")]
    [InlineData("https://www.geoguessr.com/de/challenge/AbCdEf123456?utm=x", ReviewLinkKind.Challenge, "AbCdEf123456")]
    [InlineData("geoguessr.com/challenge/AbCdEf123456", ReviewLinkKind.Challenge, "AbCdEf123456")]
    [InlineData("https://www.geoguessr.com/game/dhmguJ9rQTqI6cmA", ReviewLinkKind.Game, "dhmguJ9rQTqI6cmA")]
    [InlineData("https://www.geoguessr.com/results/eOb5HPFMPcMjJ5oM", ReviewLinkKind.Results, "eOb5HPFMPcMjJ5oM")]
    [InlineData("  AbCdEf123456  ", ReviewLinkKind.Results, "AbCdEf123456")]
    [InlineData("https://maps.app.goo.gl/dfn7LaomB9WMUtKu5", ReviewLinkKind.StreetViewShortLink, "https://maps.app.goo.gl/dfn7LaomB9WMUtKu5")]
    [InlineData(IssueLink, ReviewLinkKind.StreetView, IssueLink)]
    public void ReviewLinks_ReadWhatAMemberPastes(string text, ReviewLinkKind kind, string value) =>
        ReviewLinks.Read(text).Should().Be(new ReviewLink(kind, value));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("today's daily")]
    [InlineData("https://www.geoguessr.com/maps/world")]
    [InlineData("abc")]
    public void ReviewLinks_ReadNothing_FromAnythingElse(string? text) =>
        ReviewLinks.Read(text).Should().BeNull();
}
