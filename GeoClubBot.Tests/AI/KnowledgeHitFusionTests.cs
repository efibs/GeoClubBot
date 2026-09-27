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

    private static WeightedHitList List(double weight, params KnowledgeHit[] hits) => new(hits, weight);

    private static KnowledgeHit Hit(string text) =>
        new(Guid.NewGuid(), 0.5f, KnowledgeChunkKind.Text, text, "https://guide/text", ImageUrl: null,
            "Guide", "country", "Guide > Section", "Author", 0);

    private static KnowledgeHit Picture(string text) =>
        new(Guid.NewGuid(), 0.2f, KnowledgeChunkKind.Image, text, "https://guide/picture", $"https://img/{Guid.NewGuid():N}.png",
            "Guide", "country", "Guide > Section", "Author", 0);
}
