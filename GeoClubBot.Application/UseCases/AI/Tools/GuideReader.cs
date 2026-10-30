using UseCases.OutputPorts.AI;

namespace UseCases.UseCases.AI.Tools;

/// <summary>
/// Picks what the read-the-guide tool shows of a guide: the parts nearest the excerpt it was asked
/// about, as many as fit.
///
/// The index stores no reading order, so nearness is read from what it does store. The section path
/// comes first: the rest of the excerpt's own section, then its sibling sections, then the sections it
/// sits in or holds, then the rest of the guide. Within one of those, the local key decides: chunk keys
/// are paragraph and slide numbers in Google documents ("p12", "p12#1") and stable ids elsewhere, so
/// ordered naturally they put a document's neighbouring paragraphs side by side, and they never hurt
/// where they mean nothing. The chosen chunks are then shown in that order, as they read in the guide.
///
/// Pure, so the choice is tested without an index.
/// </summary>
public static class GuideReader
{
    private const string SectionSeparator = " > ";

    /// <param name="anchor">The excerpt the model asked about.</param>
    /// <param name="chunks">Every chunk of its guide the index holds, in any order.</param>
    /// <param name="ledger">What has been offered already, which is never shown again.</param>
    /// <param name="maxChunks">Most chunks returned.</param>
    /// <param name="maxCharacters">Most text returned, all chunks together; the first chunk is returned whatever its length.</param>
    public static IReadOnlyList<KnowledgeHit> SelectAround(
        KnowledgeHit anchor,
        IReadOnlyList<KnowledgeHit> chunks,
        ExcerptLedger ledger,
        int maxChunks,
        int maxCharacters)
    {
        ArgumentNullException.ThrowIfNull(anchor);
        ArgumentNullException.ThrowIfNull(chunks);
        ArgumentNullException.ThrowIfNull(ledger);

        var inOrder = chunks
            .Where(chunk => chunk.Id != anchor.Id)
            .OrderBy(chunk => chunk.LocalKey ?? string.Empty, NaturalKeyComparer.Instance)
            .ThenBy(chunk => chunk.Id)
            .ToList();

        // Where the anchor would stand among them, so "next to it" means the same thing on both sides.
        var anchorKey = anchor.LocalKey ?? string.Empty;
        var anchorPosition = inOrder.Count(chunk => NaturalKeyComparer.Instance.Compare(chunk.LocalKey ?? string.Empty, anchorKey) < 0);

        var candidates = inOrder
            .Select((chunk, position) => (Chunk: chunk, Position: position))
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate.Chunk.Text)
                                // A picture whose only text is a heading tells the model nothing.
                                && !candidate.Chunk.IsHeadingOnlyPicture
                                && !ledger.Holds(candidate.Chunk))
            .OrderBy(candidate => Relation(anchor.SectionPath, candidate.Chunk.SectionPath))
            .ThenBy(candidate => Math.Abs(candidate.Position - anchorPosition + (candidate.Position >= anchorPosition ? 1 : 0)))
            // As far before as after: what follows, which is where a reader goes on.
            .ThenBy(candidate => candidate.Position >= anchorPosition ? 0 : 1)
            .ThenBy(candidate => candidate.Position)
            .ToList();

        var chosen = new List<(KnowledgeHit Chunk, int Position)>();
        var seenTexts = new HashSet<string>(StringComparer.Ordinal);
        var characters = 0;

        foreach (var candidate in candidates)
        {
            if (chosen.Count >= Math.Max(1, maxChunks))
            {
                break;
            }

            // Two copies of one paragraph in the same guide are one paragraph to the reader.
            if (!seenTexts.Add(Collapse(candidate.Chunk.Text)))
            {
                continue;
            }

            var length = candidate.Chunk.Text.Length;
            if (chosen.Count > 0 && characters + length > maxCharacters)
            {
                continue;
            }

            chosen.Add(candidate);
            characters += length;
        }

        return [.. chosen.OrderBy(choice => choice.Position).Select(choice => choice.Chunk)];
    }

    /// <summary>
    /// How far one section is from another: 0 the same, 1 a sibling, 2 one inside the other, 3 sharing
    /// an outer section, 4 unrelated.
    /// </summary>
    internal static int Relation(string? anchorPath, string? path)
    {
        var anchor = Segments(anchorPath);
        var other = Segments(path);

        var shared = 0;
        while (shared < anchor.Length && shared < other.Length
               && string.Equals(anchor[shared], other[shared], StringComparison.OrdinalIgnoreCase))
        {
            shared++;
        }

        if (shared == anchor.Length && shared == other.Length)
        {
            return 0;
        }

        if (anchor.Length == other.Length && shared == anchor.Length - 1)
        {
            return 1;
        }

        if (shared == anchor.Length || shared == other.Length)
        {
            return 2;
        }

        return shared > 0 ? 3 : 4;
    }

    private static string[] Segments(string? path) =>
        string.IsNullOrWhiteSpace(path)
            ? []
            : path.Split(SectionSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string Collapse(string text) =>
        string.Join(' ', text.Split(default(char[]), StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();

    /// <summary>Orders "p2" before "p10" and "p12#1" right after "p12": digit runs compare as numbers.</summary>
    internal sealed class NaturalKeyComparer : IComparer<string>
    {
        public static readonly NaturalKeyComparer Instance = new();

        public int Compare(string? x, string? y)
        {
            x ??= string.Empty;
            y ??= string.Empty;

            int i = 0, j = 0;
            while (i < x.Length && j < y.Length)
            {
                if (char.IsAsciiDigit(x[i]) && char.IsAsciiDigit(y[j]))
                {
                    var startX = i;
                    var startY = j;
                    while (i < x.Length && char.IsAsciiDigit(x[i]))
                    {
                        i++;
                    }

                    while (j < y.Length && char.IsAsciiDigit(y[j]))
                    {
                        j++;
                    }

                    var numberX = x[startX..i].TrimStart('0');
                    var numberY = y[startY..j].TrimStart('0');
                    var byLength = numberX.Length.CompareTo(numberY.Length);
                    if (byLength != 0)
                    {
                        return byLength;
                    }

                    var byDigits = string.CompareOrdinal(numberX, numberY);
                    if (byDigits != 0)
                    {
                        return byDigits;
                    }

                    continue;
                }

                var byCharacter = x[i].CompareTo(y[j]);
                if (byCharacter != 0)
                {
                    return byCharacter;
                }

                i++;
                j++;
            }

            return (x.Length - i).CompareTo(y.Length - j) is var rest and not 0
                ? rest
                : string.CompareOrdinal(x, y);
        }
    }
}
