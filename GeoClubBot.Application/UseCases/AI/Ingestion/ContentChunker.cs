using System.Text;
using UseCases.OutputPorts.AI.Ingestion;

namespace UseCases.UseCases.AI.Ingestion;

/// <summary>
/// Budgets are in characters rather than tokens. The embedding model's tokenizer is not available
/// locally, and roughly four characters per token is close enough for sizing when the model's own
/// window (131k) is orders of magnitude larger than any single chunk.
/// </summary>
public sealed record ChunkingOptions
{
    /// <summary>Preferred size, about 350 tokens.</summary>
    public int TargetCharacters { get; init; } = 1_400;

    /// <summary>Hard ceiling before a chunk is split.</summary>
    public int MaxCharacters { get; init; } = 2_000;

    /// <summary>Below this a chunk is merged forward; a fragment on its own retrieves poorly.</summary>
    public int MinCharacters { get; init; } = 160;

    /// <summary>Repeated across a split boundary so a sentence spanning it is still findable.</summary>
    public int OverlapCharacters { get; init; } = 200;
}

/// <summary>
/// Normalises extractor output into chunks that embed well.
///
/// The previous implementation had no chunking at all — one vector per section div, whatever its
/// size — so a long section produced one diluted vector and a one-line section produced a vector with
/// almost no signal.
///
/// Local keys stay stable: split parts append an index to the original key and merges keep the first
/// key, so re-ingesting unchanged content lands on the same point ids and updates in place.
/// </summary>
public static class ContentChunker
{
    public static IReadOnlyList<ExtractedChunk> Chunk(
        IReadOnlyList<ExtractedChunk> chunks,
        ChunkingOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        options ??= new ChunkingOptions();

        var expanded = new List<ExtractedChunk>();
        foreach (var chunk in chunks)
        {
            // Image chunks are atomic: their text is a caption that belongs to one picture, and
            // splitting or merging it would attach that picture to the wrong words.
            if (chunk.ImageUrl is not null)
            {
                expanded.Add(chunk with { Text = chunk.Text.Trim() });
                continue;
            }

            expanded.AddRange(Split(chunk, options));
        }

        return MergeUndersized(expanded, options);
    }

    private static IEnumerable<ExtractedChunk> Split(ExtractedChunk chunk, ChunkingOptions options)
    {
        var text = chunk.Text.Trim();
        if (text.Length <= options.MaxCharacters)
        {
            return [chunk with { Text = text }];
        }

        var parts = new List<ExtractedChunk>();
        var paragraphs = text.Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var builder = new StringBuilder();

        foreach (var paragraph in paragraphs)
        {
            // A single paragraph over the ceiling has no internal boundary to respect, so it is cut
            // on length; anything else would leave a chunk too large to embed well.
            if (paragraph.Length > options.MaxCharacters)
            {
                Flush(parts, chunk, builder);
                foreach (var slice in HardSplit(paragraph, options))
                {
                    parts.Add(Part(chunk, parts.Count, slice));
                }

                continue;
            }

            if (builder.Length > 0 && builder.Length + paragraph.Length > options.TargetCharacters)
            {
                Flush(parts, chunk, builder);
                AppendOverlap(builder, parts[^1].Text, options.OverlapCharacters);
            }

            if (builder.Length > 0)
            {
                builder.Append("\n\n");
            }

            builder.Append(paragraph);
        }

        Flush(parts, chunk, builder);
        return parts;
    }

    private static IEnumerable<string> HardSplit(string paragraph, ChunkingOptions options)
    {
        for (var offset = 0; offset < paragraph.Length; offset += options.MaxCharacters)
        {
            yield return paragraph.Substring(offset, Math.Min(options.MaxCharacters, paragraph.Length - offset));
        }
    }

    private static void Flush(List<ExtractedChunk> parts, ExtractedChunk source, StringBuilder builder)
    {
        if (builder.Length == 0)
        {
            return;
        }

        parts.Add(Part(source, parts.Count, builder.ToString().Trim()));
        builder.Clear();
    }

    /// <summary>Carries the tail of the previous part forward so a sentence split across the boundary still matches.</summary>
    private static void AppendOverlap(StringBuilder builder, string previous, int overlap)
    {
        if (overlap <= 0 || previous.Length == 0)
        {
            return;
        }

        builder.Append(previous[^Math.Min(overlap, previous.Length)..]).Append("\n\n");
    }

    private static ExtractedChunk Part(ExtractedChunk source, int index, string text) =>
        source with { LocalKey = $"{source.LocalKey}#{index}", Text = text };

    /// <summary>
    /// Folds fragments into the next chunk of the same section. A stray one-line chunk embeds to
    /// almost nothing and crowds out a real answer.
    /// </summary>
    private static List<ExtractedChunk> MergeUndersized(List<ExtractedChunk> chunks, ChunkingOptions options)
    {
        var merged = new List<ExtractedChunk>();

        for (var index = 0; index < chunks.Count; index++)
        {
            var current = chunks[index];

            while (current.ImageUrl is null
                   && current.Text.Length < options.MinCharacters
                   && index + 1 < chunks.Count
                   && chunks[index + 1].ImageUrl is null
                   && chunks[index + 1].SectionPath == current.SectionPath
                   && current.Text.Length + chunks[index + 1].Text.Length <= options.MaxCharacters)
            {
                // The merged chunk keeps the earlier key, so the surviving point's identity is the
                // one a reader would expect.
                current = current with { Text = $"{current.Text}\n\n{chunks[index + 1].Text}" };
                index++;
            }

            merged.Add(current);
        }

        return merged;
    }
}
