using FluentAssertions;
using UseCases.OutputPorts.AI;
using UseCases.UseCases.AI.Conversations;
using Xunit;

namespace GeoClubBot.Tests.AI;

/// <summary>
/// "Can you please show me an image of the exact distribution?" was answered "I don't have the actual
/// distribution image": the follow-up's own search offered one Mongolian excerpt of eight, and not the
/// map the answer before had cited as [1]. A follow-up is offered what that answer cited first.
/// </summary>
public sealed class CarriedExcerptsTests
{
    [Fact]
    public void Combine_OffersTheCarriedExcerptsFirst_InTheirOrder()
    {
        var map = Picture("Mongolia Gen 4: four straps near Khorgo.");
        var tent = Text("Open tent coverage bridges the two strap sections.");
        var found = Text("Mongolia's Gen 4 car has a tent on its roof.");

        var combined = CarriedExcerpts.Combine([map, tent], [found], maxCarried: 6);

        combined.Hits.Should().Equal(map, tent, found);
        combined.Carried.Should().Be(2);
    }

    [Fact]
    public void Combine_OffersWhatTheNewSearchFindsAgain_OnceUnderItsCarriedNumber()
    {
        var map = Picture("Mongolia Gen 4: four straps near Khorgo.");
        var sameChunk = map with { Score = 0.9f };
        var sameText = Text("Open tent coverage bridges the two strap sections.");
        var copyOfIt = Text("open  tent coverage bridges the two strap SECTIONS.");
        var other = Text("Khorgo lies west of Tsetserleg.");

        var combined = CarriedExcerpts.Combine([map, sameText], [sameChunk, copyOfIt, other], maxCarried: 6);

        combined.Hits.Should().Equal(map, sameText, other);
    }

    [Fact]
    public void Combine_KnowsACopyMergedIntoACarriedExcerpt()
    {
        var copyId = Guid.NewGuid();
        var slide = Picture("Brazilian Poles Lite > Slide 7") with { MergedChunkIds = [copyId] };
        var copy = Picture("Brazilian Poles Lite > Slide 7 — Roraima") with { Id = copyId };

        CarriedExcerpts.Combine([slide], [copy], maxCarried: 6).Hits.Should().Equal(slide);
    }

    [Fact]
    public void Combine_StillOffersAPictureBesideCarriedTextItShares()
    {
        // As for a tool's results: the picture brings something the model can attach.
        var text = Text("Sabah lamp posts have two small circles.");
        var picture = Picture("Sabah lamp posts have two small circles.");

        CarriedExcerpts.Combine([text], [picture], maxCarried: 6).Hits.Should().Equal(text, picture);
    }

    [Fact]
    public void Combine_CarriesAtMostTheLimit_InTheOrderTheyWereCited()
    {
        var cited = Enumerable.Range(1, 5).Select(index => Text($"Cited excerpt {index}.")).ToList();

        var combined = CarriedExcerpts.Combine(cited, [Text("Found now.")], maxCarried: 3);

        combined.Hits.Select(hit => hit.Text).Should().Equal("Cited excerpt 1.", "Cited excerpt 2.", "Cited excerpt 3.", "Found now.");
        combined.Carried.Should().Be(3);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Combine_CarriesNothing_WhenTurnedOff(int maxCarried)
    {
        var found = Text("Found now.");

        var combined = CarriedExcerpts.Combine([Text("Cited.")], [found], maxCarried);

        combined.Hits.Should().Equal(found);
        combined.Carried.Should().Be(0);
    }

    [Fact]
    public void Combine_LeavesAFreshQuestionsExcerptsExactlyAsTheSearchGaveThem()
    {
        // The same text twice is the search's to collapse; nothing carried means nothing to touch.
        var retrieved = new[] { Text("Bollards are white."), Text("Bollards are white.") };

        var combined = CarriedExcerpts.Combine([], retrieved, maxCarried: 6);

        combined.Hits.Should().BeSameAs(retrieved);
        combined.Carried.Should().Be(0);
    }

    [Fact]
    public void Combine_SaysWhichNumberTheReaderSawEachUnder_WhenTwoChunksOfOnePageSharedOne()
    {
        // The reader saw both chunks of the Doc section as [1], the picture as [2] and the tip as [3].
        // Carried one number each, "show [2] bigger" pointed the model at a text chunk.
        var chunkA = Text("Four straps near Khorgo.", "https://docs.google.com/document/d/mongolia");
        var chunkB = Text("Three straps further east.", "https://docs.google.com/document/d/mongolia");
        var picture = Picture("Tent map.");
        var tip = Text("Open tents bridge the strap sections.", "https://www.plonkit.net/mongolia#t1");
        IReadOnlyList<Guid> cited = [chunkA.Id, chunkB.Id, picture.Id, tip.Id];

        var combined = CarriedExcerpts.Combine([chunkA, chunkB, picture, tip], [Text("Found now.", "https://x")], maxCarried: 6, cited);

        combined.Carried.Should().Be(4);
        combined.CarriedSourceNumbers.Should().Equal(1, 1, 2, 3);
    }

    [Fact]
    public void Combine_NumbersTwoSectionsOfOnePage_AsTheReaderSawThem_OneEach()
    {
        // The reader sees a Google Doc's sections as lines of their own (CitationResolver.KeyOf), so a
        // carried chunk from another section of the same Doc keeps a number of its own.
        var chunkA = Text("Four straps near Khorgo.", "https://docs.google.com/document/d/mongolia", "Mongolia Gen 4 Car Meta > Four Straps");
        var chunkB = Text("Three straps further east.", "https://docs.google.com/document/d/mongolia", "Mongolia Gen 4 Car Meta > Four Straps");
        var chunkC = Text("The tent is open.", "https://docs.google.com/document/d/mongolia", "Mongolia Gen 4 Car Meta > Open Tent");
        var picture = Picture("Tent map.");
        IReadOnlyList<Guid> cited = [chunkA.Id, chunkB.Id, chunkC.Id, picture.Id];

        var combined = CarriedExcerpts.Combine([chunkA, chunkB, chunkC, picture], [], maxCarried: 6, cited);

        combined.CarriedSourceNumbers.Should().Equal(1, 1, 2, 3);
    }

    [Fact]
    public void Combine_SaysNoNumbers_WhenTheReaderSawTheCarriedOnes()
    {
        var text = Text("Four straps near Khorgo.", "https://docs.google.com/document/d/mongolia");
        var picture = Picture("Tent map.");

        CarriedExcerpts.Combine([text, picture], [], maxCarried: 6, [text.Id, picture.Id])
            .CarriedSourceNumbers.Should().BeNull("[1] and [2] already mean what the reader saw");
    }

    [Fact]
    public void Combine_SaysNoNumbers_WhenACitedChunkDidNotComeBack()
    {
        // A guide re-indexed since, or a species range that never was in the index: every number after
        // the missing one would be off by one.
        var chunkA = Text("Four straps near Khorgo.", "https://docs.google.com/document/d/mongolia");
        var chunkB = Text("Three straps further east.", "https://docs.google.com/document/d/mongolia");
        var picture = Picture("Tent map.");

        CarriedExcerpts.Combine([chunkA, chunkB, picture], [], maxCarried: 6, [chunkA.Id, Guid.NewGuid(), chunkB.Id, picture.Id])
            .CarriedSourceNumbers.Should().BeNull();
    }

    [Fact]
    public void Combine_NumbersOnlyTheCarriedExcerptsItKeeps()
    {
        var chunkA = Text("Four straps near Khorgo.", "https://docs.google.com/document/d/mongolia");
        var chunkB = Text("Three straps further east.", "https://docs.google.com/document/d/mongolia");
        var picture = Picture("Tent map.");

        var combined = CarriedExcerpts.Combine([chunkA, chunkB, picture], [], maxCarried: 2, [chunkA.Id, chunkB.Id, picture.Id]);

        combined.Hits.Should().Equal(chunkA, chunkB);
        combined.CarriedSourceNumbers.Should().Equal(1, 1);
    }

    private static KnowledgeHit Text(string text) => Text(text, "https://www.plonkit.net/mongolia#a");

    private static KnowledgeHit Text(string text, string sourceUrl, string section = "Mongolia > Car metas") =>
        new(Guid.NewGuid(), 0f, KnowledgeChunkKind.Text, text, sourceUrl, null,
            "Mongolia", "mongolia", section, null, 0, "plonkit", "mongolia");

    private static KnowledgeHit Picture(string text) =>
        new(Guid.NewGuid(), 0f, KnowledgeChunkKind.Image, text, "https://docs.google.com/document/d/mongolia", $"https://relay/{Guid.NewGuid():N}.png",
            "Mongolia Gen 4 Car Meta", "mongolia", "Mongolia Gen 4 Car Meta", null, 0, "gdoc", "mongolia");
}
