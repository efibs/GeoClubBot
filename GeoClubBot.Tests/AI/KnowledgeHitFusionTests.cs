using FluentAssertions;
using Infrastructure.OutputAdapters.AI;
using UseCases.OutputPorts.AI;
using Xunit;

namespace GeoClubBot.Tests.AI;

/// <summary>
/// The weights and the collapsing come from replaying the beta testers' questions against the
/// production index; these pin the arithmetic that replay settled on.
/// </summary>
public sealed class KnowledgeHitFusionTests
{
    private const double CrossModal = 0.25;

    [Fact]
    public void Fuse_LetsTextOutrankAPictureFoundOnlyByItsPixels()
    {
        // At equal weight the picture would tie the best text match. Its caption has nothing to do
        // with the question, so the model could say nothing about it: three such pictures of Ugandan
        // forest pushed out the Vietnam guides on Khasi pines.
        var khasi = Hit("Khasi pines are common in Meghalaya.");
        var vietnam = Hit("Plantations of khasi pines are also common in central Vietnam.");
        var forest = Picture("The final type of coverage is a number of tiny forest paths.");

        var fused = KnowledgeHitFusion.Fuse([List(1d, khasi, vietnam), List(CrossModal, forest)], limit: 8);

        fused.Select(hit => hit.Id).Should().Equal(khasi.Id, vietnam.Id, forest.Id);
    }

    [Fact]
    public void Fuse_StillLiftsAChunkBothListsAgreeOn()
    {
        // "White car long antenna" ranks 39th by its text and first by its picture; between them it is
        // the answer, and it should beat a text match ranked 6th that the pictures do not back up.
        var textList = Enumerable.Range(0, 40).Select(index => Hit($"text match {index}")).ToList();
        var antenna = textList[38];

        var fused = KnowledgeHitFusion.Fuse(
            [new WeightedHitList(textList, 1d), List(CrossModal, antenna)], limit: 8);

        fused.Select(hit => hit.Id).ToList().IndexOf(antenna.Id).Should().BeInRange(0, 5);
    }

    [Fact]
    public void Fuse_MatchesTheStoresOwnFusion_WhenTheListsWeighTheSame()
    {
        // 1/(rank + 2), as Qdrant's RRF: a hit third by text and second by picture (0.25 + 0.33) beats
        // one first in a single list (0.5), and ties go to the earlier list.
        var both = Hit("in both lists");
        var textOnly = Hit("first by text");
        var imageOnly = Picture("first by picture");
        var filler = Hit("second by picture");

        var fused = KnowledgeHitFusion.Fuse(
            [List(1d, textOnly, filler, both), List(1d, imageOnly, both)], limit: 8);

        fused.Select(hit => hit.Id).Should().Equal(both.Id, textOnly.Id, imageOnly.Id, filler.Id);
        fused[0].Score.Should().BeApproximately((float)(1d / 4 + 1d / 3), 1e-6f);
    }

    [Fact]
    public void Fuse_CollapsesExcerptsWithTheSameText()
    {
        // Two authors' region guides carry the same sentence, and it took three of the eight slots.
        var first = Hit("3. Gen 4 \"shitcam\": Found in most of North-Central Province.");
        var copy = Hit("3. Gen 4 \"shitcam\": Found in most of North-Central Province.");
        var other = Hit("Lebanon is the only Middle Eastern country with shitcam.");

        var fused = KnowledgeHitFusion.Fuse([List(1d, first, copy, other)], limit: 8);

        fused.Select(hit => hit.Id).Should().Equal(first.Id, other.Id);
    }

    [Fact]
    public void Fuse_KeepsThePicture_WhenALaterCopyBringsOne()
    {
        // The same caption as prose and as a picture of Khasi pines: one excerpt, and the one the model
        // can attach, in the place the better-ranked copy earned.
        var prose = Hit("Khasi pine - super common in west Meghalaya.");
        var other = Hit("Region guessing Meghalaya is fairly easy.");
        var picture = Picture("Khasi pine - super common in west Meghalaya.");

        var fused = KnowledgeHitFusion.Fuse([List(1d, prose, other, picture)], limit: 8);

        fused.Should().HaveCount(2);
        fused[0].Id.Should().Be(picture.Id);
        fused[0].ImageUrl.Should().Be(picture.ImageUrl);
        fused[0].Score.Should().BeApproximately(0.5f, 1e-6f, "it keeps the better copy's place");
    }

    [Fact]
    public void Fuse_KeepsEveryPictureOfCopiesWithTheSameCaption()
    {
        // A slide's caption sits beside each of its pictures — eight on "India Poles > Slide 18" — and
        // keeping only the best-ranked copy's picture left 72 % of slide pictures impossible to offer.
        const string caption = "India Poles > Slide 18: concrete poles with a single crossbar.";
        var first = Picture(caption);
        var other = Hit("Kerala uses tall poles.");
        var second = Picture(caption);
        var third = Picture(caption);

        var fused = KnowledgeHitFusion.Fuse([List(1d, first, other, second, third)], limit: 8);

        fused.Should().HaveCount(2);
        fused[0].Id.Should().Be(first.Id);
        fused[0].ImageUrl.Should().Be(first.ImageUrl);
        fused[0].ExtraImageUrls.Should().Equal([second.ImageUrl!, third.ImageUrl!], "best-ranked copy first");
        fused[0].MergedChunkIds.Should().Equal(second.Id, third.Id);
        fused[0].ChunkIds.Should().Equal(first.Id, second.Id, third.Id);
    }

    [Fact]
    public void Fuse_KeepsTheIdsOfTheTextCopiesItCollapses_AndAddsNoPictureToText()
    {
        var first = Hit("Gen 4 is found in most of North-Central Province.");
        var copy = Hit("Gen 4 is found in most of North-Central Province.");

        var fused = KnowledgeHitFusion.Fuse([List(1d, first, copy)], limit: 8).Single();

        fused.ImageUrl.Should().BeNull();
        fused.ExtraImageUrls.Should().BeEmpty();
        fused.ChunkIds.Should().Equal(first.Id, copy.Id);
    }

    [Fact]
    public void Fuse_LetsAPictureLeadTheTextItCaptions_AndKeepsTheOtherPictures()
    {
        // Prose first, then two pictures with the same text: the excerpt becomes the first picture —
        // one the model can attach — and brings the second too.
        var prose = Hit("Snow poles in Finland have one thin white band.");
        var picture = Picture("Snow poles in Finland have one thin white band.");
        var another = Picture("Snow poles in Finland have one thin white band.");

        var fused = KnowledgeHitFusion.Fuse([List(1d, prose, picture, another)], limit: 8).Single();

        fused.Id.Should().Be(picture.Id);
        fused.ExtraImageUrls.Should().Equal(another.ImageUrl);
        fused.ChunkIds.Should().BeEquivalentTo([prose.Id, picture.Id, another.Id]);
    }

    [Fact]
    public void Fuse_ComparesTextAsAReaderWould()
    {
        var plain = Hit("Khasi pines are mainly found in Meghalaya.");
        var marked = Hit("**[Khasi pines](https://en.wikipedia.org/wiki/Pinus_kesiya)** are  mainly found in MEGHALAYA.");

        KnowledgeHitFusion.Fuse([List(1d, plain, marked)], limit: 8).Should().ContainSingle();
    }

    [Fact]
    public void Fuse_NeverCollapsesEmptyTexts()
    {
        // Nothing is known to be the same about two excerpts that say nothing.
        KnowledgeHitFusion.Fuse([List(1d, Hit(""), Hit("   "))], limit: 8).Should().HaveCount(2);
    }

    [Fact]
    public void Fuse_StopsAtTheLimit_ButStillTakesAPictureFromALaterCopy()
    {
        var kept = Hit("Red soil is most common in the northeast.");
        var second = Hit("Laterite roads are orange.");
        var beyondTheLimit = Hit("Something else entirely.");
        var picture = Picture("Red soil is most common in the northeast.");

        var fused = KnowledgeHitFusion.Fuse([List(1d, kept, second, beyondTheLimit, picture)], limit: 2);

        fused.Select(hit => hit.Id).Should().Equal(picture.Id, second.Id);
    }

    [Fact]
    public void Fuse_ReturnsNothing_ForNoLists() =>
        KnowledgeHitFusion.Fuse([], limit: 8).Should().BeEmpty();

    [Fact]
    public void Fuse_DemotesAPictureWhoseOnlyTextIsAHeading()
    {
        // Five of the eight excerpts offered for "How can I clearly identify Sabah?" were pictures
        // captioned only "Svalbard — Identifying Svalbard" and the like: the model can say nothing
        // about them, yet the heading matched the question first.
        var heading = Picture("Svalbard — Identifying Svalbard", title: "Svalbard — Identifying Svalbard");
        var sabah = Hit("Sabah has unique parallel pole tops not seen in mainland Malaysia.");

        KnowledgeHitFusion.Fuse([List(1d, heading, sabah)], limit: 8)
            .Select(hit => hit.Id).Should().Equal(heading.Id, sabah.Id);

        var demoted = KnowledgeHitFusion.Fuse([List(1d, heading, sabah)], limit: 8, headingOnlyPictureWeight: 0.5);
        demoted.Select(hit => hit.Id).Should().Equal(sabah.Id, heading.Id);
        demoted[1].Score.Should().BeApproximately((float)(0.5 / 2), 1e-6f);
    }

    [Fact]
    public void Fuse_LeavesACaptionedPictureWhereItRanks()
    {
        var captioned = Picture("JKR signs in Sabah have a yellow border, unlike the mainland's.");
        var text = Hit("Sabah has unique parallel pole tops.");

        KnowledgeHitFusion.Fuse([List(1d, captioned, text)], limit: 8, headingOnlyPictureWeight: 0)
            .Select(hit => hit.Id).Should().Equal(captioned.Id, text.Id);
    }

    [Theory]
    [InlineData("Svalbard — Identifying Svalbard", "Svalbard — Identifying Svalbard", "Svalbard > Identifying Svalbard", true)]
    [InlineData("Identifying Svalbard", "Svalbard — Identifying Svalbard", "Svalbard > Identifying Svalbard", true)]
    [InlineData("Mongolia — Region-specific clues", "Plonk It", "Mongolia > Region-specific clues", true)]
    [InlineData("Distribution", "USA Cars", "USA Cars > Distribution", true)]
    [InlineData("Summary :", "Kazakhstan", "Kazakhstan > Summary :", true)]
    [InlineData("https://i.imgur.com/AgNVVGk.png", "Mongolia", "Mongolia", true)]
    [InlineData("The tent with 4 straps covers most of central-northern Mongolia.", "Plonk It", "Mongolia > Region-specific clues", false)]
    public void IsHeadingOnlyPicture_KnowsACaptionThatSaysNothing(string text, string title, string section, bool expected) =>
        new KnowledgeHit(Guid.NewGuid(), 0.5f, KnowledgeChunkKind.Image, text, "https://guide", "https://img/x.png",
                title, "country", section, "Author", 0)
            .IsHeadingOnlyPicture.Should().Be(expected);

    [Fact]
    public void IsHeadingOnlyPicture_NeverCallsTextAPicture() =>
        Hit("Distribution").IsHeadingOnlyPicture.Should().BeFalse("prose is never demoted for being short");

    private static WeightedHitList List(double weight, params KnowledgeHit[] hits) => new(hits, weight);

    private static KnowledgeHit Hit(string text) =>
        new(Guid.NewGuid(), 0.5f, KnowledgeChunkKind.Text, text, "https://guide/text", ImageUrl: null,
            "Guide", "country", "Guide > Section", "Author", 0);

    private static KnowledgeHit Picture(string text, string title = "Guide") =>
        new(Guid.NewGuid(), 0.2f, KnowledgeChunkKind.Image, text, "https://guide/picture", $"https://img/{Guid.NewGuid():N}.png",
            title, "country", "Guide > Section", "Author", 0);
}
