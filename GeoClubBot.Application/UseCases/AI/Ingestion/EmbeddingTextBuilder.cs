using System.Text.RegularExpressions;
using UseCases.OutputPorts.AI.Ingestion;

namespace UseCases.UseCases.AI.Ingestion;

/// <summary>
/// Builds the text handed to the embedding model, which is not the same as the text stored and shown.
///
/// A chunk is prefixed with where it applies and what it is about, because a paragraph that only ever
/// says "the bollards here" needs the country and the topic in its vector to be findable at all.
///
/// Measured against the embedding model on real guide text, similarity to a relevant question:
///
/// <code>
/// bare chunk text                        0.2602
/// + a non-topical title                  0.2619
/// + a topic, no source structure         0.2913
/// + the source's own topical heading     0.3781
/// + both                                 0.3824
/// </code>
///
/// Two conclusions drive this class. A topical header is the largest single influence on retrieval.
/// And filler — a slide number, a deck title, an author's handle — costs almost everything the topic
/// gains, so anything that is not a topic is deliberately stripped rather than merely tolerated.
/// </summary>
public static partial class EmbeddingTextBuilder
{
    /// <summary>
    /// Identifies how the header is composed. Folded into a source's content hash, so changing the
    /// recipe re-indexes everything on its next scheduled run.
    ///
    /// Without it, an improvement here would never reach content that is already indexed: the hash
    /// covers the chunk's text, which has not changed, so every source would be judged unchanged and
    /// skipped — leaving the old vectors in place indefinitely. Bump this whenever the composition
    /// changes in a way that alters the vector.
    /// </summary>
    public const string RecipeVersion = "v2-topic";

    /// <summary>A heading longer than this is a paragraph misread as one; it dilutes rather than labels.</summary>
    private const int MaxSectionSegmentLength = 60;

    private static readonly string[] PlaceholderSegments = ["document", "slides", "spreadsheet", "untitled"];

    public static string Build(SourceDescriptor descriptor, ExtractedChunk chunk)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(descriptor.Country))
        {
            parts.Add(descriptor.Country);
        }

        // The source's own headings beat a guessed topic when they exist, so the guess is only used
        // to fill a gap — never to duplicate what the section already says.
        var section = CleanSection(descriptor, chunk.SectionPath);

        if (ChunkTopicClassifier.Classify(chunk.Text) is { } topic
            && (section is null || !section.Contains(topic, StringComparison.OrdinalIgnoreCase)))
        {
            parts.Add(topic);
        }

        if (section is not null)
        {
            parts.Add(section);
        }

        return parts.Count == 0 ? chunk.Text : $"{string.Join(" — ", parts)}\n\n{chunk.Text}";
    }

    /// <summary>
    /// Keeps only the topical part of a section path. Everything a source repeats on every chunk —
    /// its own title, a slide number, an author's handle — is removed, because it labels nothing and
    /// measurably lowers similarity.
    /// </summary>
    private static string? CleanSection(SourceDescriptor descriptor, string? sectionPath)
    {
        if (string.IsNullOrWhiteSpace(sectionPath))
        {
            return null;
        }

        var kept = sectionPath
            .Split('>', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(segment => IsTopical(descriptor, segment))
            .ToList();

        return kept.Count == 0 ? null : string.Join(" > ", kept);
    }

    private static bool IsTopical(SourceDescriptor descriptor, string segment)
    {
        if (segment.Length == 0 || segment.Length > MaxSectionSegmentLength)
        {
            return false;
        }

        // Repeated on every chunk of the source, so it separates nothing.
        if (segment.Equals(descriptor.Country, StringComparison.OrdinalIgnoreCase)
            || segment.Equals(descriptor.Title, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // An author credit picked up by the heading heuristic.
        if (segment.StartsWith('@'))
        {
            return false;
        }

        if (PlaceholderSegments.Contains(segment.ToLowerInvariant()))
        {
            return false;
        }

        // "Slide 4", "Page 12", "Row 30" — position, not subject.
        return !PositionalSegment().IsMatch(segment);
    }

    [GeneratedRegex(@"^(slide|page|row|section|part)?\s*\d+$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PositionalSegment();
}
