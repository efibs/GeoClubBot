using FluentAssertions;
using UseCases.OutputPorts.AI.Ingestion;
using UseCases.UseCases.AI.Ingestion;
using Xunit;

namespace GeoClubBot.Tests.AI;

public sealed class ContentChunkerTests
{
    private static readonly ChunkingOptions Options = new()
    {
        TargetCharacters = 200,
        MaxCharacters = 300,
        MinCharacters = 40,
        OverlapCharacters = 30
    };

    [Fact]
    public void Chunk_LeavesWellSizedTextAlone()
    {
        var input = new[] { Text("a", new string('x', 150)) };

        var result = ContentChunker.Chunk(input, Options);

        result.Should().ContainSingle();
        result[0].LocalKey.Should().Be("a", "an unsplit chunk keeps its original key");
    }

    [Fact]
    public void Chunk_SplitsOversizedTextOnParagraphBoundaries()
    {
        var paragraphs = string.Join("\n\n", Enumerable.Repeat(new string('x', 120), 6));

        var result = ContentChunker.Chunk([Text("a", paragraphs)], Options);

        result.Should().HaveCountGreaterThan(1);
        result.Should().OnlyContain(chunk => chunk.Text.Length <= Options.MaxCharacters);
        result.Select(chunk => chunk.LocalKey).Should().OnlyHaveUniqueItems();
        result.Should().OnlyContain(chunk => chunk.LocalKey.StartsWith("a#"));
    }

    [Fact]
    public void Chunk_SplitsAParagraphThatHasNoInternalBoundary()
    {
        // A single run of text over the ceiling has nowhere natural to break, so it is cut on length
        // rather than left too large to embed well.
        var result = ContentChunker.Chunk([Text("a", new string('x', 1_000))], Options);

        result.Should().HaveCountGreaterThan(1);
        result.Should().OnlyContain(chunk => chunk.Text.Length <= Options.MaxCharacters);
    }

    [Fact]
    public void Chunk_CarriesOverlapAcrossASplit()
    {
        // Without overlap, a sentence spanning the boundary matches neither side.
        var paragraphs = string.Join("\n\n", Enumerable.Repeat(new string('x', 120), 4));

        var result = ContentChunker.Chunk([Text("a", paragraphs)], Options);

        result.Should().HaveCountGreaterThan(1);
        result[1].Text.Should().StartWith(new string('x', Options.OverlapCharacters));
    }

    [Fact]
    public void Chunk_NeverSplitsOrMergesAnImage()
    {
        // An image's text is the caption for that one picture; merging it into a neighbour would
        // attach the picture to the wrong words.
        var input = new[]
        {
            Image("img", "tiny", "https://i.imgur.com/a.png"),
            Text("after", new string('y', 50))
        };

        var result = ContentChunker.Chunk(input, Options);

        result.Should().HaveCount(2);
        result[0].LocalKey.Should().Be("img");
        result[0].ImageUrl.Should().Be("https://i.imgur.com/a.png");
    }

    [Fact]
    public void Chunk_MergesFragmentsWithinTheSameSection()
    {
        // A one-line chunk embeds to almost nothing and would crowd out a real answer.
        var input = new[] { Text("a", "short"), Text("b", "also short"), Text("c", new string('x', 100)) };

        var result = ContentChunker.Chunk(input, Options);

        result.Should().ContainSingle();
        result[0].LocalKey.Should().Be("a", "a merged chunk keeps the earliest key");
        result[0].Text.Should().Contain("short").And.Contain("also short");
    }

    [Fact]
    public void Chunk_DoesNotMergeAcrossSections()
    {
        var input = new[]
        {
            Text("a", "short", section: "Guide > One"),
            Text("b", "also short", section: "Guide > Two")
        };

        var result = ContentChunker.Chunk(input, Options);

        result.Should().HaveCount(2, "merging across sections would blend unrelated advice");
    }

    [Fact]
    public void Chunk_IsDeterministic()
    {
        // Chunk ids feed the vector store's point ids. If the same input produced different keys on
        // different runs, every re-ingest would duplicate rather than update.
        var input = new[]
        {
            Text("a", string.Join("\n\n", Enumerable.Repeat(new string('x', 120), 5))),
            Image("b", "caption", "https://i.imgur.com/b.png")
        };

        var first = ContentChunker.Chunk(input, Options).Select(chunk => (chunk.LocalKey, chunk.Text));
        var second = ContentChunker.Chunk(input, Options).Select(chunk => (chunk.LocalKey, chunk.Text));

        second.Should().Equal(first);
    }

    [Fact]
    public void Chunk_HandlesAnEmptyDocument() =>
        ContentChunker.Chunk([], Options).Should().BeEmpty();

    private static readonly ChunkingOptions Tight = new()
    {
        TargetCharacters = 15,
        MaxCharacters = 20,
        MinCharacters = 0,
        OverlapCharacters = 0
    };

    private static string Of(char letter, int count) => new(letter, count);

    private static IReadOnlyList<string> Parts(string text, ChunkingOptions? options = null) =>
        ContentChunker.Chunk([Text("a", text)], options ?? Tight).Select(chunk => chunk.Text).ToList();

    [Fact]
    public void Chunk_JoinsParagraphsWithABlankLine_WhileTheyFitTheTarget()
    {
        var result = ContentChunker.Chunk([Text("a", "aaaaa\n\nbbbbb\n\ncccccccccc\n\ndddddddddd")], Tight);

        result.Select(chunk => chunk.Text).Should().Equal("aaaaa\n\nbbbbb", "cccccccccc", "dddddddddd");
        result.Select(chunk => chunk.LocalKey).Should().Equal("a#0", "a#1", "a#2");
    }

    [Fact]
    public void Chunk_FillsTheTargetExactly()
    {
        var options = Tight with { TargetCharacters = 10 };

        Parts("aaaaa\n\nbbbbb\n\ncccccccccc", options).Should().Equal("aaaaa\n\nbbbbb", "cccccccccc");
    }

    [Fact]
    public void Chunk_TrimsEachParagraph()
    {
        Parts("  aaaaaaaaaaaaaaa  \n\n   bbbbbbbbbbbbbbb  ").Should().Equal(Of('a', 15), Of('b', 15));
    }

    [Fact]
    public void Chunk_CarriesTheTailOfThePreviousPart_IntoTheNext()
    {
        var parts = Parts("aaaaaaab\n\ncccccccc\n\ndddddddd", Tight with { TargetCharacters = 10, OverlapCharacters = 3 });

        parts[0].Should().Be("aaaaaaab");
        parts[1].Should().StartWith("aab\n\n").And.EndWith("cccccccc");
    }

    [Fact]
    public void Chunk_CarriesAShortPreviousPartWhole()
    {
        var parts = Parts("aaaaaaab\n\ncccccccc\n\ndddddddd", Tight with { TargetCharacters = 10, OverlapCharacters = 100 });

        parts[1].Should().StartWith("aaaaaaab\n\n");
    }

    [Fact]
    public void Chunk_CarriesNothing_WithoutAnOverlap()
    {
        Parts("aaaaaaaa\n\ncccccccc\n\ndddddddd", Tight with { TargetCharacters = 10 })
            .Should().Equal("aaaaaaaa", "cccccccc", "dddddddd");
    }

    [Fact]
    public void Chunk_KeepsTextOfExactlyTheMaximumWhole()
    {
        var result = ContentChunker.Chunk([Text("a", Of('x', 20))], Tight);

        result.Should().ContainSingle().Which.LocalKey.Should().Be("a");
    }

    [Fact]
    public void Chunk_CutsAParagraphOverTheMaximumByLength_InOrder()
    {
        Parts("aaaaa\n\n" + Of('x', 25) + "\n\nbbbbb").Should().Equal("aaaaa", Of('x', 20), Of('x', 5), "bbbbb");
    }

    [Fact]
    public void Chunk_LeavesNoEmptyPart_WhenAParagraphIsAnExactMultipleOfTheMaximum()
    {
        Parts(Of('x', 40)).Should().Equal(Of('x', 20), Of('x', 20));
    }

    [Fact]
    public void Chunk_TreatsAParagraphOfExactlyTheMaximumLikeAnyOther()
    {
        var parts = Parts(Of('a', 20) + "\n\nbbbbb", Tight with { OverlapCharacters = 3 });

        parts[1].Should().StartWith("aaa\n\n");
    }

    [Fact]
    public void Chunk_MergesARunOfFragments_UnderTheFirstKey()
    {
        var options = Tight with { MinCharacters = 8 };

        var result = ContentChunker.Chunk([Text("a", "aa"), Text("b", "bb"), Text("c", "cc"), Text("d", Of('d', 9))], options);

        // Merging stops once the minimum is reached, so the long chunk stays on its own.
        result.Select(chunk => chunk.Text).Should().Equal("aa\n\nbb\n\ncc", Of('d', 9));
        result[0].LocalKey.Should().Be("a");
    }

    [Fact]
    public void Chunk_LeavesAChunkOfExactlyTheMinimum_UnmergedWithTheNext()
    {
        var options = Tight with { MinCharacters = 5 };

        ContentChunker.Chunk([Text("a", "aaaaa"), Text("b", "bbbbb")], options).Should().HaveCount(2);
    }

    [Fact]
    public void Chunk_MergesUpToExactlyTheMaximum_ButNotPastIt()
    {
        var options = Tight with { MinCharacters = 8 };

        ContentChunker.Chunk([Text("a", "aaaaa"), Text("b", Of('b', 15))], options).Should().ContainSingle();
        ContentChunker.Chunk([Text("a", "aaaaa"), Text("b", Of('b', 16))], options).Should().HaveCount(2);
    }

    [Fact]
    public void Chunk_NeverMergesIntoAPicture()
    {
        var options = Tight with { MinCharacters = 8 };

        ContentChunker.Chunk([Text("a", "aa"), Image("b", "caption", "https://img/1.png")], options).Should().HaveCount(2);
    }

    [Fact]
    public void Chunk_NeedsChunks()
    {
        var act = () => ContentChunker.Chunk(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    private static ExtractedChunk Text(string key, string text, string section = "Guide > Section") =>
        new(key, section, text);

    private static ExtractedChunk Image(string key, string caption, string imageUrl, string section = "Guide > Section") =>
        new(key, section, caption, imageUrl);
}
