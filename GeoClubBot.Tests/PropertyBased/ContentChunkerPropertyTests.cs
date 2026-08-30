using CsCheck;
using FluentAssertions;
using UseCases.OutputPorts.AI.Ingestion;
using UseCases.UseCases.AI.Ingestion;
using Xunit;

namespace GeoClubBot.Tests.PropertyBased;

/// <summary>
/// Invariants of the chunker over arbitrary documents. These are the properties that quietly break
/// ingestion rather than failing loudly: an oversized chunk embeds badly, and an unstable key turns
/// every re-ingest into a pile of duplicates instead of an update.
/// </summary>
public sealed class ContentChunkerPropertyTests
{
    private static readonly ChunkingOptions Options = new()
    {
        TargetCharacters = 200,
        MaxCharacters = 300,
        MinCharacters = 40,
        OverlapCharacters = 30
    };

    /// <summary>Documents of a few paragraphs of varying length, some of them images.</summary>
    private static readonly Gen<List<ExtractedChunk>> GenDocument =
        Gen.Select(
                Gen.Int[0, 3],
                Gen.Int[0, 900],
                Gen.Int[0, 1],
                (section, length, isImage) => new ExtractedChunk(
                    LocalKey: $"k{section}-{length}-{isImage}",
                    SectionPath: $"Guide > Section {section}",
                    Text: string.Join("\n\n", Enumerable.Repeat(new string('x', Math.Max(1, length / 5)), 5)),
                    ImageUrl: isImage == 1 ? "https://i.imgur.com/x.png" : null))
            .List[0, 8]
            // Keys must be unique within a document, as they are in every real extractor.
            .Select(chunks => chunks
                .GroupBy(chunk => chunk.LocalKey)
                .Select(group => group.First())
                .ToList());

    [Fact]
    public void No_chunk_exceeds_the_configured_ceiling() =>
        GenDocument.Sample(document =>
        {
            var result = ContentChunker.Chunk(document, Options);

            result.Where(chunk => chunk.ImageUrl is null)
                .Should().OnlyContain(chunk => chunk.Text.Length <= Options.MaxCharacters);
        });

    [Fact]
    public void Chunk_keys_stay_unique_so_points_cannot_collide() =>
        GenDocument.Sample(document =>
        {
            var result = ContentChunker.Chunk(document, Options);

            result.Select(chunk => chunk.LocalKey).Should().OnlyHaveUniqueItems();
        });

    [Fact]
    public void Chunking_is_stable_across_runs() =>
        GenDocument.Sample(document =>
        {
            var first = ContentChunker.Chunk(document, Options).Select(chunk => chunk.LocalKey).ToList();
            var second = ContentChunker.Chunk(document, Options).Select(chunk => chunk.LocalKey).ToList();

            second.Should().Equal(first);
        });

    [Fact]
    public void Every_image_survives_chunking() =>
        GenDocument.Sample(document =>
        {
            var result = ContentChunker.Chunk(document, Options);

            result.Count(chunk => chunk.ImageUrl is not null)
                .Should().Be(document.Count(chunk => chunk.ImageUrl is not null));
        });
}
