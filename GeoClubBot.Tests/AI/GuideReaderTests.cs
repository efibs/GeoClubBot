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

    // Which of two chunks is nearer shows in which one a single-chunk read picks.
    [Theory]
    [InlineData("A > B", "A > B", "A > C")]
    [InlineData("A > B", "a > b", "A > C")]
    [InlineData("A > B", "A > C", "A > B > D")]
    [InlineData("A > B", "A > C", "A")]
    [InlineData("A > B", "A", "A > C > D")]
    [InlineData("A > B > C", "A > B", "A > X")]
    [InlineData("A > B", "A > B > C", "A > X > Y")]
    [InlineData("A > B > C", "A > X", "Z")]
    [InlineData("A > B", "A > X > Y", "Z")]
    [InlineData("A > B", null, "A > X > Y")]
    public void ReadsTheNearerSectionFirst(string anchorPath, string? nearer, string? farther)
    {
        var anchor = Piece("anchor", section: anchorPath);
        var near = Piece("near", section: nearer);

        var chosen = GuideReader.SelectAround(anchor, [Piece("far", section: farther), near], Ledger(), 1, 1000);

        chosen.Should().Equal(near);
    }

    [Fact]
    public void TreatsTwoChunksWithoutASection_AsTheSameSection()
    {
        var anchor = Piece("anchor", section: null);
        var near = Piece("near", section: "  ");

        var chosen = GuideReader.SelectAround(anchor, [Piece("far", section: "A"), near], Ledger(), 1, 1000);

        chosen.Should().Equal(near);
    }

    [Fact]
    public void IgnoresTheSpacingAroundASectionName()
    {
        var anchor = Piece("anchor", section: "A > B");
        var near = Piece("near", section: "A >   B  ");

        var chosen = GuideReader.SelectAround(anchor, [Piece("far", section: "A > C"), near], Ledger(), 1, 1000);

        chosen.Should().Equal(near);
    }

    [Fact]
    public void GoesOnAfterTheExcerpt_WhenTwoParagraphsAreEquallyNear()
    {
        var anchor = Piece("anchor", localKey: "p5");
        var before = Piece("before", localKey: "p4");
        var after = Piece("after", localKey: "p6");

        var chosen = GuideReader.SelectAround(anchor, [before, after], Ledger(), 1, 1000);

        chosen.Should().Equal(after);
    }

    [Fact]
    public void TakesTheNearerParagraph_OverOneStraightAfterTheExcerptOnlyWhenItIsNearer()
    {
        var anchor = Piece("anchor", localKey: "p5");
        var before = Piece("before", localKey: "p4");
        var wayAfter = Piece("way after", localKey: "p9");
        var after = Piece("after", localKey: "p6");

        GuideReader.SelectAround(anchor, [wayAfter, before, after], Ledger(), 2, 1000).Should().Equal(before, after);
    }

    [Fact]
    public void ReturnsTheChosenChunksInReadingOrder()
    {
        var anchor = Piece("anchor", localKey: "p5");
        var first = Piece("first", localKey: "p4");
        var second = Piece("second", localKey: "p6");

        GuideReader.SelectAround(anchor, [second, first], Ledger(), 2, 1000).Should().Equal(first, second);
    }

    [Theory]
    [InlineData("p2", "p10")]
    [InlineData("p12", "p12#1")]
    [InlineData("p12#1", "p13")]
    [InlineData("p9", "p010")]
    [InlineData("a", "b")]
    [InlineData("p1", "pa")]
    public void OrdersKeysNaturally(string earlier, string later)
    {
        var first = Piece("first", localKey: earlier);
        var second = Piece("second", localKey: later);

        GuideReader.SelectAround(Piece("anchor", localKey: "z99"), [second, first], Ledger(), 2, 1000)
            .Should().Equal(first, second);
    }

    [Fact]
    public void ReadsChunksWithoutAKey_FirstAmongThemAll()
    {
        var keyless = Piece("keyless", localKey: null);
        var keyed = Piece("keyed", localKey: "p1");

        GuideReader.SelectAround(Piece("anchor", localKey: "p5"), [keyed, keyless], Ledger(), 2, 1000)
            .Should().Equal(keyless, keyed);
    }

    [Fact]
    public void NeverOffersTheExcerptItself()
    {
        var anchor = Piece("anchor");

        GuideReader.SelectAround(anchor, [anchor], Ledger(), 5, 1000).Should().BeEmpty();
    }

    [Fact]
    public void ShowsTwoCopiesOfAParagraphOnce_WhateverTheirCaseAndSpacing()
    {
        var chosen = GuideReader.SelectAround(
            Piece("anchor"),
            [Piece("Red  bollards", localKey: "p1"), Piece("red bollards", localKey: "p2"), Piece("poles", localKey: "p3")],
            Ledger(), 5, 1000);

        chosen.Select(chunk => chunk.Text).Should().Equal("Red  bollards", "poles");
    }

    [Fact]
    public void SkipsABlankChunk()
    {
        GuideReader.SelectAround(Piece("anchor"), [Piece("  ")], Ledger(), 5, 1000).Should().BeEmpty();
    }

    [Fact]
    public void FillsTheCharacterCapExactly()
    {
        var chosen = GuideReader.SelectAround(
            Piece("anchor"),
            [Piece("12345", localKey: "p1"), Piece("67890", localKey: "p2"), Piece("abcde", localKey: "p3")],
            Ledger(), 5, 10);

        chosen.Select(chunk => chunk.Text).Should().Equal("12345", "67890");
    }

    [Fact]
    public void StepsOverAChunkThatDoesNotFit_ToAShorterOneThatDoes()
    {
        var chosen = GuideReader.SelectAround(
            Piece("anchor", localKey: "p0"),
            [Piece("12345", localKey: "p1"), Piece("far too long to fit", localKey: "p2"), Piece("abc", localKey: "p3")],
            Ledger(), 5, 10);

        chosen.Select(chunk => chunk.Text).Should().Equal("12345", "abc");
    }

    [Fact]
    public void AlwaysReturnsAtLeastOneChunk_WhateverTheChunkLimit()
    {
        GuideReader.SelectAround(Piece("anchor"), [Piece("only")], Ledger(), 0, 1000).Should().ContainSingle();
    }

    [Fact]
    public void NeedsAnExcerptChunksAndALedger()
    {
        var chunk = Piece("x");

        ((Action)(() => GuideReader.SelectAround(null!, [], Ledger(), 1, 1))).Should().Throw<ArgumentNullException>();
        ((Action)(() => GuideReader.SelectAround(chunk, null!, Ledger(), 1, 1))).Should().Throw<ArgumentNullException>();
        ((Action)(() => GuideReader.SelectAround(chunk, [], null!, 1, 1))).Should().Throw<ArgumentNullException>();
    }

    private static ExcerptLedger Ledger(params KnowledgeHit[] offered) => new(offered);

    private static KnowledgeHit Piece(string text, string? localKey = "p1", string? section = "Section") =>
        new(Guid.NewGuid(), 0f, KnowledgeChunkKind.Text, text, "https://docs.google.com/document/d/guide/edit", null,
            "Guide", "brazil", section, null, 0, "gdoc", "guide", localKey);

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
