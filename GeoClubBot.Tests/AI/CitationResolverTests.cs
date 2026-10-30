using FluentAssertions;
using UseCases.UseCases.AI.Conversations;
using Xunit;

namespace GeoClubBot.Tests.AI;

/// <summary>
/// Every citation style here is one a free model actually wrote in an answer beta testers rated. The
/// previous handling knew two of them, and the rest reached users as "see the plate in ****", "(, )",
/// a literal "image 1" with no picture, and numbers running [6] [1] [4].
/// </summary>
public sealed class CitationResolverTests
{
    private static readonly IReadOnlyList<OfferedExcerpt> EightTextExcerpts =
        [.. Enumerable.Range(1, 8).Select(marker => Text(marker, $"https://guide/{marker}"))];

    [Fact]
    public void Resolve_RenumbersCitationsInOrderOfFirstMention()
    {
        // "The citation style is hard to follow. Especially because the sources are not ordered by number."
        var result = CitationResolver.Resolve("A [6]. B [1]. C [4]. Again A [6].", EightTextExcerpts, maxImages: 3);

        result.Text.Should().Be("A [1]. B [2]. C [3]. Again A [1].");
        result.Sources.Select(source => source.Number).Should().Equal(1, 2, 3);
        result.Sources.Select(source => source.Excerpt.Marker).Should().Equal(6, 1, 4);
    }

    [Fact]
    public void Resolve_WritesARunOfCitationsInAscendingOrder()
    {
        var result = CitationResolver.Resolve("First [3]. Both [4, 3]. Glued [5][2].", EightTextExcerpts, maxImages: 3);

        result.Text.Should().Be("First [1]. Both [1][2]. Glued [3][4].");
    }

    [Theory]
    [InlineData("Blurred most of the time【image 1】.", "Blurred most of the time[1].")]
    [InlineData("The shirorekha is a key feature【2】.", "The shirorekha is a key feature[1].")]
    [InlineData("A full-width［3］ bracket.", "A full-width[1] bracket.")]
    [InlineData("A tortoise-shell 〔4〕 bracket.", "A tortoise-shell [1] bracket.")]
    public void Resolve_ReadsFullWidthBrackets(string answer, string expected)
    {
        // A reasoning model wrote 【image 1】, which the old pattern did not know — the tester saw
        // "image 1" in the text and no picture.
        var offered = new[] { Picture(1, "https://guide/ghana", "https://img/truck.png"), Text(2, "https://guide/2"),
            Text(3, "https://guide/3"), Text(4, "https://guide/4") };

        var result = CitationResolver.Resolve(answer, offered, maxImages: 3);

        result.Text.Should().Be(expected);
        result.Sources.Should().ContainSingle();
    }

    [Theory]
    [InlineData("Common in Southland [1, 3, 5].", "Common in Southland [1][2][3].", 3)]
    [InlineData("Near Arequipa [7-8].", "Near Arequipa [1][2].", 2)]
    [InlineData("Near Arequipa [7–8].", "Near Arequipa [1][2].", 2)]
    [InlineData("Either one [2 and 4].", "Either one [1][2].", 2)]
    [InlineData("A footnote[^5].", "A footnote[1].", 1)]
    public void Resolve_ReadsListsRangesAndFootnotes(string answer, string expected, int sources)
    {
        var result = CitationResolver.Resolve(answer, EightTextExcerpts, maxImages: 3);

        result.Text.Should().Be(expected);
        result.Sources.Should().HaveCount(sources);
    }

    [Fact]
    public void Resolve_AttachesAPictureCitedAsAnImage()
    {
        var offered = new[] { Text(1, "https://guide/tunisia"), Picture(2, "https://guide/tunisia#map", "https://img/map.png") };

        var result = CitationResolver.Resolve("The codes run by governorate [image 2].", offered, maxImages: 3);

        result.Text.Should().Be("The codes run by governorate [1].");
        result.Images.Should().ContainSingle().Which.Excerpt.ImageUrl.Should().Be("https://img/map.png");
    }

    [Fact]
    public void Resolve_AttachesAPictureCitedByItsPlainNumber()
    {
        // "I would have liked to have the PlonkIt image for license plates attached directly to the
        // answer" — the model had cited the picture as a plain [1], which used to attach nothing.
        var offered = new[] { Picture(1, "https://www.plonkit.net/mexico#ah2z", "https://img/plates.png") };

        var result = CitationResolver.Resolve("Learn the state designs first [1].", offered, maxImages: 3);

        result.Images.Should().ContainSingle();
        result.Images[0].Number.Should().Be(1, "the embed carries the same number as the prose");
    }

    [Fact]
    public void Resolve_TreatsAnImageMarkerOnTextAsAnOrdinaryCitation()
    {
        // The old resolver deleted "[image N]" whatever N was, so a text excerpt cited that way
        // vanished from the prose and from the source list alike.
        var result = CitationResolver.Resolve("Serbia uses the smallcam [image 2].", EightTextExcerpts, maxImages: 3);

        result.Text.Should().Be("Serbia uses the smallcam [1].");
        result.Sources.Should().ContainSingle().Which.Excerpt.SourceUrl.Should().Be("https://guide/2");
        result.Images.Should().BeEmpty();
    }

    [Fact]
    public void Resolve_ListsPicturesAmongTheSources_SoTheNumbersStayContiguous()
    {
        var offered = new[] { Text(1, "https://guide/a"), Picture(2, "https://guide/b", "https://img/b.png"), Text(3, "https://guide/c") };

        var result = CitationResolver.Resolve("One [1]. Two [2]. Three [3].", offered, maxImages: 3);

        result.Sources.Select(source => (source.Number, source.IsImage))
            .Should().Equal((1, false), (2, true), (3, false));
    }

    [Fact]
    public void Resolve_GivesOneNumberToExcerptsFromTheSameSectionOfAPage()
    {
        // Chunks of one Google Doc share a link: "(and duplicate ****)" was a model noticing two
        // numbers that led to the same place, under the same heading.
        var offered = new[]
        {
            new OfferedExcerpt(2, "https://docs/california", "California > Cacti"),
            new OfferedExcerpt(5, "https://docs/california", "California > Cacti")
        };

        var result = CitationResolver.Resolve("Saguaros grow here [2] (and here [5]).", offered, maxImages: 3);

        result.Text.Should().Be("Saguaros grow here [1] (and here [1]).");
        result.Sources.Should().ContainSingle();
    }

    [Fact]
    public void Resolve_GivesEachSectionOfOneDocumentItsOwnNumber()
    {
        // Every text chunk of a Google Doc shares the document's one link. Keyed by the link alone, an
        // answer naming nine US states cited them all as one "[2] … Counties (33) ->" (2026-10-29).
        var offered = new[]
        {
            new OfferedExcerpt(2, "https://docs/usa", "USA · A Unique USA > Front Plate Requirements"),
            new OfferedExcerpt(5, "https://docs/usa", "USA · A Unique USA > Counties (33)"),
            new OfferedExcerpt(7, "https://docs/usa", "USA · A Unique USA > Counties (33)")
        };

        var result = CitationResolver.Resolve("Front plates [2]. Counties [5], and more counties [7].", offered, maxImages: 3);

        result.Text.Should().Be("Front plates [1]. Counties [2], and more counties [2].");
        result.Sources.Select(source => source.Excerpt.Label)
            .Should().Equal("USA · A Unique USA > Front Plate Requirements", "USA · A Unique USA > Counties (33)");
        result.CitedExcerpts.Select(excerpt => excerpt.Marker).Should().Equal(2, 5, 7);
    }

    [Theory]
    // #3 of the 2026-10-29 replay, as the model wrote it.
    [InlineData("Rounded tiles are common in central India [1].\n\n[1] (picture, usually with a distribution map) shows the tile style.",
        "Rounded tiles are common in central India [1].\n\n[1] shows the tile style.")]
    [InlineData("The plate [2] (picture) is yellow.", "The plate [1] is yellow.")]
    [InlineData("See the slide (3 pictures) [2].", "See the slide [1].")]
    [InlineData("The map **(picture)** [2].", "The map [1].")]
    [InlineData("As before [1] (the user saw this as 2).", "As before [1].")]
    [InlineData("The tip [2], usually with a distribution map, shows it.", "The tip [1] shows it.")]
    [InlineData("The tip [2] (usually with a distribution map).", "The tip [1].")]
    public void Resolve_RemovesThePromptsNotesOnAnExcerpt(string answer, string expected)
    {
        var offered = new[] { Text(1, "https://guide/1"), Picture(2, "https://guide/2", "https://img/2.png") };

        CitationResolver.Resolve(answer, offered, maxImages: 3).Text.Should().Be(expected);
    }

    [Fact]
    public void Resolve_LeavesThePromptsNotesInCodeAlone()
    {
        CitationResolver.Resolve("Write `(picture)` for one.", EightTextExcerpts, maxImages: 3).Text
            .Should().Be("Write `(picture)` for one.");
    }

    [Theory]
    // #13 of the 2026-10-29 replay: the model's "([1]–[6])" reached the reader as "([5]–[4])".
    [InlineData("Clues [3][4][5][6]. Any of the pictured ones ([1]–[6]) will do.",
        "Clues [1][2][3][4]. Any of the pictured ones [1][2][3][4][5][6] will do.", 6)]
    [InlineData("Near Arequipa [7]-[8].", "Near Arequipa [1][2].", 2)]
    [InlineData("Near Arequipa [2] – [4].", "Near Arequipa [1][2][3].", 3)]
    [InlineData("Backwards [6]—[2], never a range.", "Backwards [1][2], never a range.", 2)]
    [InlineData("Too wide [1]–[20] to be one.", "Too wide [1] to be one.", 1)]
    // Each end in emphasis of its own: read as two runs, this came out "Then [2]–[1] are pictured."
    [InlineData("First [6]. Then **[1]**–**[6]** are pictured.", "First [1]. Then [1][2][3][4][5][6] are pictured.", 6)]
    [InlineData("First [6]. Then *[1]* – [6] are pictured.", "First [1]. Then [1][2][3][4][5][6] are pictured.", 6)]
    [InlineData("Pictured: (**[2]**–**[4]**).", "Pictured: [1][2][3].", 3)]
    public void Resolve_ReadsTwoCitationsJoinedByADash_AsTheRangeTheyMean(string answer, string expected, int sources)
    {
        var result = CitationResolver.Resolve(answer, EightTextExcerpts, maxImages: 3);

        result.Text.Should().Be(expected);
        result.Sources.Should().HaveCount(sources);
    }

    [Fact]
    public void Resolve_KeepsTextAndPictureFromOnePageApart()
    {
        var offered = new[] { Text(1, "https://guide/thailand"), Picture(2, "https://guide/thailand", "https://img/tuk.png") };

        var result = CitationResolver.Resolve("Tuk-tuks [1], pictured [2].", offered, maxImages: 3);

        result.Sources.Should().HaveCount(2);
        result.Images.Should().ContainSingle();
    }

    [Theory]
    [InlineData("As described in [9].", "As described in.")]
    [InlineData("The plate [9] is white.", "The plate is white.")]
    [InlineData("[9] Norway has pines.", "Norway has pines.")]
    [InlineData("Characteristic of the post-Ural region ([9], [10]).", "Characteristic of the post-Ural region.")]
    [InlineData("Combed to one side [image 9] .", "Combed to one side .")]
    public void Resolve_RemovesInventedNumbers_WithoutLeavingAHole(string answer, string expected)
    {
        // Only numbers that were offered survive; anything else points nowhere. Taken out with the
        // space in front of it — the last case keeps the model's own stray space, which is not ours.
        var result = CitationResolver.Resolve(answer, [Text(1, "https://guide/1")], maxImages: 3);

        result.Text.Should().Be(expected);
        result.Sources.Should().BeEmpty();
    }

    [Fact]
    public void Resolve_UnwrapsEmphasisAroundACitation()
    {
        // "→ See the Google car's plate in ****": the model bolded [image 3], and deleting the marker
        // left the asterisks behind.
        var offered = new[] { Text(1, "https://guide/1"), Text(2, "https://guide/2"), Picture(3, "https://guide/th", "https://img/plate.png") };

        var result = CitationResolver.Resolve("See the Google car's plate in **[image 3]**, a Thai plate.", offered, maxImages: 3);

        result.Text.Should().Be("See the Google car's plate in [1], a Thai plate.");
        result.Images.Should().ContainSingle();
    }

    [Fact]
    public void Resolve_DropsParenthesesHoldingOnlyCitations()
    {
        // "…highly characteristic of the post-Ural region (, )." was two stripped image markers.
        var result = CitationResolver.Resolve(
            "Highly characteristic of the post-Ural region ([image 2], [image 5]).", EightTextExcerpts, maxImages: 3);

        result.Text.Should().Be("Highly characteristic of the post-Ural region [1][2].");
    }

    [Fact]
    public void Resolve_DropsALineOfNothingButCitations_ButStillCountsThem()
    {
        // "[2] [5] [8]" as a closing line: the model listing its sources, which the list under the
        // answer already does — but they were still the sources it meant.
        var result = CitationResolver.Resolve(
            "Southern Chelyabinsk and the Far East [2].\n\n[2] [5] [8]", EightTextExcerpts, maxImages: 3);

        result.Text.Should().Be("Southern Chelyabinsk and the Far East [1].");
        result.Sources.Should().HaveCount(3);
    }

    [Fact]
    public void Resolve_DropsASourcesLine()
    {
        var result = CitationResolver.Resolve("Pampas grass [3].\n\n**Sources:** [3], [4]", EightTextExcerpts, maxImages: 3);

        result.Text.Should().Be("Pampas grass [1].");
    }

    [Theory]
    [InlineData("A [link](https://example.com) and [1](https://example.com).")]
    [InlineData("Renamed in [2019] from Compostela Valley.")]
    [InlineData("Index [0] and range [1:2] are not citations.")]
    [InlineData("Plates use `[A-Z]{2} [0-9]{4}` as a pattern.")]
    [InlineData("```\nsee [3]\n```")]
    [InlineData("An [image] with no number.")]
    [InlineData("The sign in image 8 is Bengali.")]
    public void Resolve_LeavesWhatIsNotACitationAlone(string answer)
    {
        var result = CitationResolver.Resolve(answer, EightTextExcerpts, maxImages: 3);

        result.Text.Should().Be(answer);
        result.Sources.Should().BeEmpty();
    }

    [Fact]
    public void Resolve_UnwrapsACitationFormattedAsCode()
    {
        // Left as code it would keep the model's number, which after renumbering points elsewhere.
        var result = CitationResolver.Resolve("First [5], then `[3]`.", EightTextExcerpts, maxImages: 3);

        result.Text.Should().Be("First [1], then [2].");
    }

    [Theory]
    [InlineData("Pink taxis 【4:0†source】.")]
    [InlineData("Pink taxis 【4†Thailand guide】.")]
    public void Resolve_ReadsOpenAiStyleMarkers(string answer)
    {
        var result = CitationResolver.Resolve(answer, EightTextExcerpts, maxImages: 3);

        result.Text.Should().Be("Pink taxis [1].");
        result.Sources.Single().Excerpt.Marker.Should().Be(4);
    }

    [Fact]
    public void Resolve_CapsAttachedPictures_ButStillListsThem()
    {
        var offered = new[]
        {
            Picture(1, "https://guide/a", "https://img/a.png"),
            Picture(2, "https://guide/b", "https://img/b.png"),
            Picture(3, "https://guide/c", "https://img/c.png")
        };

        var result = CitationResolver.Resolve("[1] [2] [3] all show it.", offered, maxImages: 2);

        result.Images.Should().HaveCount(2);
        result.Sources.Should().HaveCount(3, "every number in the prose must still lead somewhere");
    }

    [Fact]
    public void Resolve_AttachesEveryPictureAnExcerptBrings_UnderItsNumber()
    {
        // One slide caption sits beside each picture of its slide; citing it once shows them all.
        var offered = new[]
        {
            Text(1, "https://guide/text"),
            Picture(2, "https://guide/slide", "https://img/1.png", "https://img/2.png", "https://img/3.png")
        };

        var result = CitationResolver.Resolve("Poles [2], per the guide [1].", offered, maxImages: 6);

        result.Text.Should().Be("Poles [1], per the guide [2].");
        result.Sources.Should().HaveCount(2, "one excerpt is one source, however many pictures it brings");
        result.Images.Select(image => (image.Number, image.ImageUrl, image.Position, image.Count)).Should().Equal(
            (1, "https://img/1.png", 1, 3),
            (1, "https://img/2.png", 2, 3),
            (1, "https://img/3.png", 3, 3));
    }

    [Fact]
    public void Resolve_ShowsEveryCitedPicturesFirstPicture_BeforeAnyExcerptsSecond()
    {
        // An answer citing two pictured excerpts should show both, not three of the first.
        var offered = new[]
        {
            Picture(1, "https://guide/a", "https://img/a1.png", "https://img/a2.png", "https://img/a3.png"),
            Picture(2, "https://guide/b", "https://img/b1.png", "https://img/b2.png")
        };

        var result = CitationResolver.Resolve("A [1], B [2].", offered, maxImages: 3);

        result.Images.Select(image => (image.Number, image.ImageUrl, image.Position, image.Count)).Should().Equal(
            (1, "https://img/a1.png", 1, 2),
            (1, "https://img/a2.png", 2, 2),
            (2, "https://img/b1.png", 1, 1));
    }

    [Fact]
    public void Resolve_AttachesAPictureTwoExcerptsShare_Once()
    {
        var offered = new[]
        {
            Picture(1, "https://guide/a", "https://img/a.png", "https://img/shared.png"),
            Picture(2, "https://guide/b", "https://img/shared.png")
        };

        var result = CitationResolver.Resolve("A [1], B [2].", offered, maxImages: 6);

        result.Images.Select(image => image.ImageUrl).Should().Equal(["https://img/a.png", "https://img/shared.png"]);
        result.Images.Select(image => image.Number).Should().Equal([1, 2], "the shared picture goes to the excerpt it leads");
    }

    [Fact]
    public void Resolve_NamesTheCitedExcerpts_InTheOrderTheReaderSeesThem()
    {
        // What a follow-up is offered again: both chunks behind [2], though they share one line.
        var offered = new[]
        {
            new OfferedExcerpt(1, "https://guide/a", "Guide A"),
            Text(2, "https://guide/b"),
            new OfferedExcerpt(3, "https://guide/a", "Guide A"),
            Text(4, "https://guide/c")
        };

        var result = CitationResolver.Resolve("C [4], A [1], B [2], A again [3]. Invented [9].", offered, maxImages: 3);

        result.Text.Should().Be("C [1], A [2], B [3], A again [2]. Invented.");
        result.CitedExcerpts.Select(excerpt => excerpt.Marker).Should().Equal(4, 1, 3, 2);
    }

    [Fact]
    public void Resolve_LeavesAnAnswerWithoutCitationsUnchanged()
    {
        const string answer = "Blue roofs are not a regional clue in Japan.\n\nThey are mostly temporary tarps.";

        var result = CitationResolver.Resolve(answer, EightTextExcerpts, maxImages: 3);

        result.Text.Should().Be(answer);
        result.Sources.Should().BeEmpty();
    }

    [Fact]
    public void Resolve_RemovesEveryCitation_WhenNothingWasOffered()
    {
        var result = CitationResolver.Resolve("Ghanaian bollards are white [1][image 2].", [], maxImages: 3);

        result.Text.Should().Be("Ghanaian bollards are white.");
        result.Sources.Should().BeEmpty();
    }

    [Fact]
    public void StripMarkers_RemovesCitationsOfEveryStyle()
    {
        CitationResolver.StripMarkers("A [1]. B **[image 2]**. C 【3】 ([4], [5]).")
            .Should().Be("A. B. C.");
    }

    [Theory]
    [MemberData(nameof(FeedbackAnswers))]
    public void Resolve_CleansUpTheAnswersBetaTestersSaw(string answer, string expected)
    {
        var result = CitationResolver.Resolve(answer, FeedbackExcerpts, maxImages: 3);

        result.Text.Should().Be(expected);
        result.Text.Should().NotContain("****").And.NotContain("(, )").And.NotContain("【");
    }

    /// <summary>
    /// Excerpt 2 and 6 are pictures, as they were in the rated answers these fragments come from.
    /// </summary>
    private static readonly IReadOnlyList<OfferedExcerpt> FeedbackExcerpts =
    [
        Text(1, "https://guide/1"),
        Picture(2, "https://guide/2", "https://img/2.png"),
        Text(3, "https://guide/3"),
        Text(4, "https://guide/4"),
        Text(5, "https://guide/5"),
        Picture(6, "https://guide/6", "https://img/6.png"),
        Text(7, "https://guide/7"),
        Text(8, "https://guide/8")
    ];

    public static TheoryData<string, string> FeedbackAnswers => new()
    {
        // #2 — a bolded picture citation
        { "→ See the Google car's plate in **[image 2]**, which shows a typical Thai plate.",
          "→ See the Google car's plate in [1], which shows a typical Thai plate." },
        // #3 — two picture citations in parentheses
        { "Highly characteristic of the **post-Ural region** ([image 2], [image 6]).",
          "Highly characteristic of the **post-Ural region** [1][2]." },
        // #8 — lists of citations
        { "\"No Indent Poles\" are common in Southland [1, 3, 5], and \"Triple Indent\" poles are rare [4].",
          "\"No Indent Poles\" are common in Southland [1][2][3], and \"Triple Indent\" poles are rare [4]." },
        // #12 — full-width brackets from a reasoning model
        { "It has a horizontal line over the letters【2】, unlike Devanagari【5】.",
          "It has a horizontal line over the letters[1], unlike Devanagari[2]." },
        // #15 — a closing line of nothing but citations
        { "Fall coverage: mainly Tula and Altai Krai [5].\n\n[2] [5] [8]",
          "Fall coverage: mainly Tula and Altai Krai [1]." }
    };

    private static OfferedExcerpt Text(int marker, string sourceUrl) =>
        new(marker, sourceUrl, $"Guide {marker}");

    private static OfferedExcerpt Picture(int marker, string sourceUrl, string imageUrl, params string[] extraImageUrls) =>
        new(marker, sourceUrl, $"Guide {marker}", imageUrl, $"Picture {marker}") { ExtraImageUrls = extraImageUrls };
}
