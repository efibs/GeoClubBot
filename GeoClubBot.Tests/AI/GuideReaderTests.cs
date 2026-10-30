using FluentAssertions;
using UseCases.OutputPorts.AI;
using UseCases.UseCases.AI.Tools;
using Xunit;

namespace GeoClubBot.Tests.AI;

/// <summary>
/// The index stores no reading order, so "the rest of the guide around this excerpt" is read from the
/// section path first and the local key second. These pin that reading, and the limits on how much of
/// a guide one read offers.
/// </summary>
public sealed class GuideReaderTests
{
    [Fact]
    public void OffersTheExcerptsOwnSectionFirst_ThenItsSiblings_ThenTheRest()
    {
        var anchor = Chunk("p10", "Mexican Plants > Baja California");
        var chunks = new[]
        {
            anchor,
            Chunk("p2", "Mexican Plants > Oaxaca"),
            Chunk("p30", "Mexican Plants > Baja California"),
            Chunk("p11", "Mexican Plants > Sonora"),
            Chunk("p40", "Mexican Plants"),
            Chunk("p50", "Cacti")
        };

        var chosen = GuideReader.SelectAround(anchor, chunks, Ledger(anchor), maxChunks: 3, maxCharacters: 6_000);

        chosen.Select(chunk => chunk.LocalKey).Should().Equal(["p2", "p11", "p30"],
            "the section's own chunk and its two siblings, shown in the order they read in the guide");
    }

    [Fact]
    public void PrefersTheNeighbouringParagraphs_WithinASection()
    {
        var anchor = Chunk("p12", "Brazil > Poles");
        var chunks = new[]
        {
            anchor,
            Chunk("p2", "Brazil > Poles"),
            Chunk("p13", "Brazil > Poles"),
            Chunk("p12#1", "Brazil > Poles"),
            Chunk("p11", "Brazil > Poles"),
            Chunk("p40", "Brazil > Poles")
        };

        var chosen = GuideReader.SelectAround(anchor, chunks, Ledger(anchor), maxChunks: 3, maxCharacters: 6_000);

        chosen.Select(chunk => chunk.LocalKey).Should().Equal(["p11", "p12#1", "p13"],
            "paragraph numbers compare as numbers, and a split part follows its paragraph");
    }

    [Fact]
    public void StopsAtTheCharacterCap_ButAlwaysOffersOne()
    {
        var anchor = Chunk("p1", "Brazil > Poles");
        var chunks = new[] { anchor, Chunk("p2", "Brazil > Poles", length: 4_000), Chunk("p3", "Brazil > Poles", length: 4_000) };

        GuideReader.SelectAround(anchor, chunks, Ledger(anchor), maxChunks: 8, maxCharacters: 6_000)
            .Should().ContainSingle().Which.LocalKey.Should().Be("p2");
        GuideReader.SelectAround(anchor, chunks, Ledger(anchor), maxChunks: 8, maxCharacters: 100)
            .Should().ContainSingle("the nearest chunk is offered whatever its length");
    }

    [Fact]
    public void SkipsWhatWasOffered_AndPicturesThatAreOnlyAHeading()
    {
        var anchor = Chunk("p1", "Brazil > Poles");
        var offered = Chunk("p2", "Brazil > Poles");
        var heading = Chunk("p3", "Brazil > Poles", kind: KnowledgeChunkKind.Image, text: "Brazil > Poles");
        var copy = Chunk("p5", "Brazil > Poles", text: offered.Text);
        var fresh = Chunk("p4", "Brazil > Poles");

        var chosen = GuideReader.SelectAround(anchor, [anchor, offered, heading, copy, fresh], Ledger(anchor, offered),
            maxChunks: 8, maxCharacters: 6_000);

        chosen.Should().Equal(fresh);
    }

    private static ExcerptLedger Ledger(params KnowledgeHit[] offered) => new(offered);

    private static KnowledgeHit Chunk(
        string localKey,
        string section,
        int length = 0,
        KnowledgeChunkKind kind = KnowledgeChunkKind.Text,
        string? text = null) =>
        new(Guid.NewGuid(), 0f, kind,
            text ?? (length > 0 ? new string('x', length) : $"Text of {localKey} under {section}."),
            "https://docs.google.com/document/d/guide/edit",
            kind == KnowledgeChunkKind.Image ? "https://relay/p.png" : null,
            "Guide", "brazil", section, null, 0, "gdoc", "guide", localKey);
}
