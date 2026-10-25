using FluentAssertions;
using UseCases.OutputPorts.AI;
using UseCases.UseCases.AI.Conversations;
using UseCases.UseCases.AI.Tools;
using Xunit;

namespace GeoClubBot.Tests.AI;

public sealed class ExcerptLedgerTests
{
    [Fact]
    public void NumbersTheRetrievedExcerpts_ExactlyAsThePromptDoes()
    {
        var hits = new[] { Text("Poles are square."), Picture("Poles", "https://relay/p.png"), Text("Poles are square.") };

        var ledger = new ExcerptLedger(hits);
        var prompt = AiPromptBuilder.Build(ConversationContext.Empty, "question", [], hits);

        ledger.Offered.Should().BeEquivalentTo(prompt.Excerpts, "copies keep their place, or every later number would be off");
        ledger.RetrievedCount.Should().Be(3);
    }

    [Fact]
    public void NumbersToolExcerpts_OnFromTheHighestMarker()
    {
        var ledger = new ExcerptLedger([Text("one"), Text("two")]);

        var offered = ledger.Offer(Text("three", "https://www.plonkit.net/brazil#3"));

        offered.Should().StartWith("[3] ").And.Contain("three").And.EndWith("(source: https://www.plonkit.net/brazil#3)");
        ledger.Offered[^1].Marker.Should().Be(3);
        ledger.HitAt(3)!.Text.Should().Be("three");
        ledger.HitAt(4).Should().BeNull();
        ledger.HitAt(0).Should().BeNull();
    }

    [Fact]
    public void SkipsWhatWasOfferedAlready_ByPointOrByText()
    {
        var retrieved = Text("Ceará poles have no supports.");
        var ledger = new ExcerptLedger([retrieved]);

        ledger.Offer(retrieved).Should().BeNull("the same point");
        ledger.Offer(Text("  ceará POLES have   no supports. ")).Should().BeNull("the same text from another copy of the guide");
        ledger.Count.Should().Be(1);
    }

    [Fact]
    public void OffersAPicture_BesideTextItShares()
    {
        var ledger = new ExcerptLedger([Text("Bollards in Ghana are white.")]);

        ledger.Offer(Picture("Bollards in Ghana are white.", "https://relay/ghana.png"))
            .Should().StartWith("[2] (picture", "it brings something the model can attach");
        ledger.Offer(Picture("Bollards in Ghana are white.", "https://relay/ghana-2.png")).Should().BeNull();
        ledger.Offer(Text("Bollards in Ghana are white.")).Should().BeNull();
    }

    [Fact]
    public void KnowsACopyMergedIntoAnOfferedExcerpt_ByItsId()
    {
        // A slide caption offered with its copies merged in: a search finding one of those copies, under
        // its own id and with its text cut differently, must not offer it again.
        var copyId = Guid.NewGuid();
        var ledger = new ExcerptLedger([Picture("India Poles > Slide 18", "https://relay/1.png") with { MergedChunkIds = [copyId] }]);

        var copy = Picture("India Poles > Slide 18 (continued)", "https://relay/2.png") with { Id = copyId };

        ledger.Holds(copy).Should().BeTrue();
        ledger.Offer(copy).Should().BeNull();
    }

    [Fact]
    public void KnowsAnExcerptWhoseMergedCopyWasOffered()
    {
        var offered = Picture("Brazilian Poles Lite > Slide 7", "https://relay/a.png");
        var ledger = new ExcerptLedger([offered]);

        var merged = Picture("Brazilian Poles Lite > Slide 7, Roraima", "https://relay/b.png") with { MergedChunkIds = [offered.Id] };

        ledger.Holds(merged).Should().BeTrue();
    }

    private static KnowledgeHit Text(string text, string url = "https://www.plonkit.net/ghana#x") =>
        new(Guid.NewGuid(), 0.5f, KnowledgeChunkKind.Text, text, url, null, "Ghana", "ghana", "Ghana > Bollards", null, 0);

    private static KnowledgeHit Picture(string text, string imageUrl) =>
        new(Guid.NewGuid(), 0.5f, KnowledgeChunkKind.Image, text, "https://www.plonkit.net/ghana#p", imageUrl, "Ghana", "ghana",
            "Ghana > Bollards", null, 0);
}
