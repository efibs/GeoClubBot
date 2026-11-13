using FluentAssertions;
using Infrastructure.OutputAdapters.AI.Extractors;
using Xunit;

namespace GeoClubBot.Tests.AI;

/// <summary>
/// How a Google document's HTML becomes paragraphs and captioned pictures. Every shape here is copied
/// from a guide of the library that answers were rated down for on 2026-11-09: a picture that reached
/// the model as a title, a paragraph filed without its subject, a heading filed without its state.
/// </summary>
public sealed class GoogleDocHtmlTests
{
    private const string Title = "Guide";

    [Fact]
    public void ACluesPictureTakesItsTitleAndItsExplanation_AndTheyAreNoParagraphsOfTheirOwn()
    {
        // "28 Secret US Metas". Captioned by its title alone, the picture was answered as "Ohio gap – a
        // telltale gap in pavement or road markings": the model made the rest up.
        var pieces = Read(
            "<h1>Useful metas</h1><h1>Bollards</h1><h1>Others</h1>"
            + "<p>Ohio gap ** (Photo : West Liberty, Ohio):</p>"
            + "<p><img src=\"ohio.png\"></p>"
            + "<p>In Ohio, you can often see gaps in road lines where there is a RPM (raised pavement marker).</p>"
            + "<p>Kentucky roads * (Photo: New Hope, Kentucky):</p>"
            + "<p><img src=\"kentucky.png\"></p>"
            + "<p>In Kentucky it is pretty common to see roads with no middle line.</p>");

        pieces.Should().HaveCount(2);
        pieces[0].ImageSource.Should().Be("ohio.png");
        pieces[0].Text.Should().Be(
            "Ohio gap ** (Photo : West Liberty, Ohio):\n"
            + "In Ohio, you can often see gaps in road lines where there is a RPM (raised pavement marker).");
        pieces[0].OwnsText.Should().BeTrue();
        pieces[1].ImageSource.Should().Be("kentucky.png");
        pieces[1].Text.Should().StartWith("Kentucky roads").And.EndWith("no middle line.");
    }

    [Fact]
    public void AParagraphAboutSomethingElse_IsNotThePicturesExplanation()
    {
        // "A Learnable Colombia": the paragraph under the white car's picture opens the next subject.
        var pieces = Read(
            "<h2>Car meta</h2><h2>Stickers</h2><h2>Lamps</h2>"
            + "<p>Colombia is the only country where you can see a black (silver) car with an antenna.</p>"
            + "<p>White car ex:</p>"
            + "<p><img src=\"white.png\"></p>"
            + "<p>In A Learnable Colombia you will mostly find Gen 4 coverage. This is because most major highways have it.</p>");

        var picture = pieces.Single(piece => piece.ImageSource is not null);
        picture.Text.Should().Be(
            "Colombia is the only country where you can see a black (silver) car with an antenna.\nWhite car ex:",
            "a lead-in that says little is given the sentence it follows");

        pieces.Where(piece => piece.ImageSource is null).Select(piece => piece.Text).Should().Equal(
            "Colombia is the only country where you can see a black (silver) car with an antenna.",
            "In A Learnable Colombia you will mostly find Gen 4 coverage. This is because most major highways have it.");
    }

    [Fact]
    public void AParagraphThatPointsBackAtThePicture_IsItsExplanation()
    {
        var pieces = Read(
            "<h1>A</h1><h1>B</h1><h1>C</h1>"
            + "<p>Utah bollard ** (Photo : Thompson Springs, Utah):</p>"
            + "<p><img src=\"utah.png\"></p>"
            + "<p>This one is unique to the state.</p>");

        pieces.Should().ContainSingle().Which.Text.Should().EndWith("This one is unique to the state.");
    }

    [Fact]
    public void PicturesStandingTogether_ShareOneCaption_AndTheCreditUnderThem()
    {
        // The map members asked for sits beside the silver car's photograph, under "Ex: Silver car".
        var pieces = Read(
            "<h2>Car meta</h2><h2>Stickers</h2><h2>Lamps</h2>"
            + "<p>You got a Gen4 white car and silver car.</p>"
            + "<p>Ex: Silver car</p>"
            + "<p><img src=\"car.png\"></p><p><img src=\"map.png\"></p>"
            + "<p>Credits to cron</p>"
            + "<p>There is also white car with a blue blur</p>"
            + "<p><img src=\"blur.png\"></p>");

        var pictures = pieces.Where(piece => piece.ImageSource is not null).ToList();
        pictures.Select(piece => piece.ImageSource).Should().Equal("car.png", "map.png", "blur.png");

        pictures[0].Text.Should().Be("You got a Gen4 white car and silver car.\nEx: Silver car\nCredits to cron");
        pictures[1].Text.Should().Be(pictures[0].Text);
        pictures[1].Group.Should().Be(pictures[0].Group);

        pictures[2].Text.Should().Be("You got a Gen4 white car and silver car.\nThere is also white car with a blue blur");
        pictures[2].Group.Should().NotBe(pictures[0].Group);

        pieces.Where(piece => piece.ImageSource is null).Select(piece => piece.Text)
            .Should().Equal(["You got a Gen4 white car and silver car."], "the sentence is lent, the lead-ins and the credit are taken");
    }

    [Fact]
    public void ALineStraightAboveThePicture_IsItsCaption_NotThePictureBeforeIts()
    {
        var pieces = Read(
            "<h1>A</h1><h1>B</h1><h1>C</h1>"
            + "<p>San Andres car:</p><p><img src=\"1.png\"></p>"
            + "<p>Providencia car:</p><p><img src=\"2.png\"></p>");

        pieces.Select(piece => (piece.ImageSource, piece.Text)).Should().Equal(
            ("1.png", "San Andres car:"),
            ("2.png", "Providencia car:"));
    }

    [Fact]
    public void APictureOpeningItsSection_IsCaptionedFromBelow()
    {
        var pieces = Read(
            "<h1>Plants</h1><h2>Boojum tree</h2>"
            + "<p><img src=\"boojum.png\"></p>"
            + "<p>Found only in the south of Baja California, on rocky slopes.</p>"
            + "<h2>Saguaro</h2><p>Sonora only.</p>");

        var picture = pieces.First();
        picture.ImageSource.Should().Be("boojum.png");
        picture.Text.Should().Be("Found only in the south of Baja California, on rocky slopes.");
        picture.SectionPath.Should().Be("Guide > Plants > Boojum tree");
    }

    [Fact]
    public void TheLastLineOfAList_IsNotAPicturesLeadIn()
    {
        // A table of contents ending "20: General Tips", then a photograph with its caption below.
        var pieces = Read(
            "<h1>A</h1><h1>B</h1><h1>C</h1>"
            + "<p>18: Spikes</p><p>19: Additional Meta</p><p>20: General Tips</p>"
            + "<p><img src=\"rock.png\"></p>"
            + "<p>The Rock of Guatape located east of Medellin</p>");

        pieces.Single(piece => piece.ImageSource is not null).Text.Should().Be("The Rock of Guatape located east of Medellin");
    }

    [Fact]
    public void ALinkAboveAPicture_IsNotItsCaption()
    {
        // Shaped as in "california: the greatest country": the heading, a maps link, the map, and the note on it.
        var pieces = Read(
            "<h1>Plants</h1><h1>Poles</h1>"
            + "<h1>Joshua Tree (Yucca brevifolia)</h1>"
            + "<p>https://maps.app.goo.gl/McLbWqPp76jDaQxF7</p>"
            + "<p><img src=\"range.png\"></p>"
            + "<p>-The only places with significant human activity and Joshua trees are in the belt circled in red.</p>");

        pieces.Single(piece => piece.ImageSource is not null).Text
            .Should().Be("-The only places with significant human activity and Joshua trees are in the belt circled in red.");
    }

    [Fact]
    public void InATableRow_TheRowsWordsCaptionItsPictures()
    {
        // "A Unique USA" lays a picture and its note out side by side.
        var pieces = Read(
            "<h1>A</h1><h1>B</h1><h1>Alaska</h1>"
            + "<p>Before the table.</p>"
            + "<table><tr><td><p><img src=\"juneau.png\"></p></td><td><p>Coverage around the state capital, Juneau.</p></td></tr>"
            + "<tr><td><p>The Dalton Highway runs north.</p></td><td><p><img src=\"dalton.png\"></p></td></tr></table>");

        pieces.Select(piece => (piece.ImageSource, piece.Text)).Should().Equal(
            (null, "Before the table."),
            ("juneau.png", "Coverage around the state capital, Juneau."),
            ("dalton.png", "The Dalton Highway runs north."));
    }

    [Fact]
    public void SectionsNestAsTheHeadingsDo_AndCarryTheNearestHeadingsLink()
    {
        // "State Route Shield" stands under every state of "A Unique USA": filed under it alone, the
        // chunk said nothing of which state.
        var pieces = Read(
            "<h1 id=\"h.md\">7.20 Maryland (MD)</h1>"
            + "<h2 id=\"h.signs\">7.20.1 Signs</h2>"
            + "<h3>State Route Shield</h3><p>White, on a black square.</p>"
            + "<h1 id=\"h.ma\">7.21 Massachusetts (MA)</h1>"
            + "<h2>7.21.1 Signs</h2>"
            + "<h3 id=\"h.shield\">State Route Shield</h3><p>Plain white.</p>");

        pieces.Select(piece => (piece.SectionPath, piece.Anchor, piece.Text)).Should().Equal(
            ("Guide > 7.20 Maryland (MD) > 7.20.1 Signs > State Route Shield", "heading=h.signs", "White, on a black square."),
            ("Guide > 7.21 Massachusetts (MA) > 7.21.1 Signs > State Route Shield", "heading=h.shield", "Plain white."));
    }

    [Fact]
    public void AHeadingAboveTheDocumentsFirst_IsHeldAtItsLevel()
    {
        // "A Learnable Colombia" has its chapters as Heading 2 and three plants as Heading 1: nested as
        // styled, "Cacti" would be filed under "Paramos".
        var pieces = Read(
            "<h2>Chapter 5: Vegetation</h2><p>Trees and nature.</p>"
            + "<h1>Paramos</h1><p>Alpine tundra.</p>"
            + "<h2>Cacti</h2><p>Found in the north-east.</p>");

        pieces.Select(piece => piece.SectionPath).Should().Equal(
            "Guide > Chapter 5: Vegetation", "Guide > Paramos", "Guide > Cacti");
    }

    [Fact]
    public void ABoldLineAboveAParagraph_IsAHeading_UnderTheStyledOneItStandsIn()
    {
        // Without it "Are most commonly found in Meta and Casanare" says nothing of what is found there.
        var pieces = Read(
            "<h2>Welcome</h2><p>Intro.</p>"
            + "<h2>Chapter 5: Vegetation</h2><p>Trees and nature.</p>"
            + "<p><span style=\"font-weight:700\">4. Oil Palms:</span></p>"
            + "<p>Are most commonly found in Meta and Casanare.</p>"
            + "<h2>Chapter 7: Bollards</h2><p>White.</p>");

        pieces.Select(piece => (piece.SectionPath, piece.Text)).Should().Equal(
            ("Guide > Welcome", "Intro."),
            ("Guide > Chapter 5: Vegetation", "Trees and nature."),
            ("Guide > Chapter 5: Vegetation > 4. Oil Palms:", "Are most commonly found in Meta and Casanare."),
            ("Guide > Chapter 7: Bollards", "White."));
    }

    [Fact]
    public void ABoldLineNumberedLikeAnOpenHeading_TakesItsPlace()
    {
        var pieces = Read(
            "<h2>Welcome</h2><p>Intro.</p>"
            + "<h2>Chapter 1: Car Meta</h2><p>Look at the car.</p>"
            + "<p><span style=\"font-weight:700\">Chapter 2: Road markers</span></p>"
            + "<p><img src=\"marker.png\"></p>"
            + "<p>Road markers tell you everything you need to know.</p>"
            + "<h2>Chapter 3: Stickers</h2><p>Stickers.</p>");

        pieces.Select(piece => (piece.SectionPath, piece.ImageSource, piece.Text)).Should().Equal(
            ("Guide > Welcome", null, "Intro."),
            ("Guide > Chapter 1: Car Meta", null, "Look at the car."),
            ("Guide > Chapter 2: Road markers", "marker.png", "Road markers tell you everything you need to know."),
            ("Guide > Chapter 3: Stickers", null, "Stickers."));
    }

    [Theory]
    // Above a picture it is the picture's caption.
    [InlineData("<p><span style=\"font-weight:700\">San Andres car</span></p><p><img src=\"a.png\"></p>")]
    // A sentence, an introduction, a credit, and rows of a list are no headings.
    [InlineData("<p><span style=\"font-weight:700\">There are 2 islands with their own google car.</span></p><p>More.</p>")]
    [InlineData("<p><span style=\"font-weight:700\">Chapters:</span></p><p>More.</p>")]
    [InlineData("<p><span style=\"font-weight:700\">Credits to cron</span></p><p>More.</p>")]
    [InlineData("<p><span style=\"font-weight:700\">Andhra Pradesh</span></p><p><span style=\"font-weight:700\">Assam</span></p><p>More.</p>")]
    // Mostly not bold.
    [InlineData("<p><span style=\"font-weight:700\">Note</span><span style=\"font-weight:400\"> that this is rare and unreliable</span></p><p>More.</p>")]
    public void ABoldLineThatIsNoHeading_OpensNoSection(string html)
    {
        var pieces = Read("<h2>One</h2><p>First.</p><h2>Two</h2><h2>Three</h2>" + html);

        pieces.Skip(1).Should().OnlyContain(piece => piece.SectionPath == "Guide > Three");
    }

    [Fact]
    public void ADocumentThatStylesNoHeadings_IsReadByTheShapeOfItsLines()
    {
        var pieces = Read(
            "<h1>Mexico Deconstructed</h1>"
            + "<p>Boojum tree</p>"
            + "<p>Found only in the south of Baja California.</p>");

        pieces.Should().ContainSingle().Which.SectionPath.Should().Be("Guide > Boojum tree");
    }

    [Fact]
    public void APicturesOwnDescription_JoinsItsCaption()
    {
        var pieces = Read(
            "<h1>A</h1><h1>B</h1><h1>C</h1>"
            + "<p>Pole tops differ by province.</p>"
            + "<p><img src=\"poles.png\" alt=\"A map of pole tops by province\"></p>");

        pieces.Should().ContainSingle().Which.Text.Should().Be("Pole tops differ by province.\nA map of pole tops by province");
    }

    [Fact]
    public void NoBreakSpaces_AreSpaces()
    {
        var pieces = Read("<p>You got a Gen4&nbsp;white car&nbsp; and silver car.</p>");

        pieces.Should().ContainSingle().Which.Text.Should().Be("You got a Gen4 white car and silver car.");
    }

    [Fact]
    public void WhatIsNoDocument_HasNoPieces()
    {
        GoogleDocHtml.Read("Tunisian roads\n\nAre numbered oddly.", Title).Should().BeEmpty();
        GoogleDocHtml.Read("<html><body><div>Sign in to continue</div></body></html>", Title).Should().BeEmpty();
    }

    private static IReadOnlyList<GoogleDocPiece> Read(string body) =>
        GoogleDocHtml.Read($"<html><body>{body}</body></html>", Title);
}
