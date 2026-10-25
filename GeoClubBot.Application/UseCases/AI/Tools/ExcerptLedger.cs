using UseCases.OutputPorts.AI;
using UseCases.UseCases.AI.Conversations;

namespace UseCases.UseCases.AI.Tools;

/// <summary>
/// Every excerpt one answer has been offered, numbered in offer order: first the ones the question was
/// retrieved with, exactly as the prompt numbered them, then what its tool calls found, numbered on
/// from the highest marker. Citations resolve against the whole list, so a tool's excerpt is cited,
/// listed and attached like any other.
///
/// A tool never offers the same thing twice. Searched again in other words, the guides mostly answer
/// with what was already offered, and an excerpt repeated under a new number is a second number for
/// one source, which the reader then sees listed twice. So a chunk already offered — the same point,
/// or the same text from another copy of a guide — is skipped. A picture is new beside text it shares,
/// as in the index's fusion: it brings something the model can attach.
/// </summary>
public sealed class ExcerptLedger
{
    private readonly List<KnowledgeHit> _hits = [];
    private readonly List<OfferedExcerpt> _offered = [];
    private readonly HashSet<Guid> _ids = [];
    private readonly HashSet<string> _texts = new(StringComparer.Ordinal);
    private readonly HashSet<string> _pictureTexts = new(StringComparer.Ordinal);

    /// <param name="retrieved">The question's excerpts, in the order the prompt numbered them from one.</param>
    public ExcerptLedger(IReadOnlyList<KnowledgeHit> retrieved)
    {
        ArgumentNullException.ThrowIfNull(retrieved);

        // Taken as they are, copies included: their markers have to be the prompt's.
        foreach (var hit in retrieved)
        {
            Add(hit);
        }

        RetrievedCount = _hits.Count;
    }

    /// <summary>Every excerpt offered, in marker order.</summary>
    public IReadOnlyList<OfferedExcerpt> Offered => _offered;

    /// <summary>The chunk behind each excerpt, in marker order: the prompt rebuilt from everything gathered.</summary>
    public IReadOnlyList<KnowledgeHit> Hits => _hits;

    /// <summary>How many of <see cref="Offered"/> came with the question rather than from a tool.</summary>
    public int RetrievedCount { get; }

    public int Count => _hits.Count;

    /// <summary>The chunk offered as <c>[marker]</c>, or null when no excerpt has that number.</summary>
    public KnowledgeHit? HitAt(int marker) => marker >= 1 && marker <= _hits.Count ? _hits[marker - 1] : null;

    /// <summary>
    /// Whether this chunk, or one with the same text, has been offered already — as an excerpt of its
    /// own or as a copy merged into one (<see cref="KnowledgeHit.MergedChunkIds"/>).
    /// </summary>
    public bool Holds(KnowledgeHit hit) =>
        hit.ChunkIds.Any(_ids.Contains)
        || (hit.Kind == KnowledgeChunkKind.Image ? _pictureTexts : _texts).Contains(TextKey(hit));

    /// <summary>
    /// Offers a chunk under the next marker and returns it as the model is shown it, or null when it
    /// was offered already.
    /// </summary>
    public string? Offer(KnowledgeHit hit)
    {
        ArgumentNullException.ThrowIfNull(hit);

        return Holds(hit) ? null : Add(hit);
    }

    private string Add(KnowledgeHit hit)
    {
        var text = AiPromptBuilder.FormatExcerpt(hit, _hits.Count + 1, out var offered);

        _hits.Add(hit);
        _offered.Add(offered);
        _ids.UnionWith(hit.ChunkIds);

        _texts.Add(TextKey(hit));
        if (hit.Kind == KnowledgeChunkKind.Image)
        {
            _pictureTexts.Add(TextKey(hit));
        }

        return text;
    }

    /// <summary>
    /// The text as a reader would compare it: case and spacing ignored. An empty text never matches
    /// another, since nothing is known to be the same about the two.
    /// </summary>
    private static string TextKey(KnowledgeHit hit)
    {
        var text = string.Join(' ', hit.Text.Split(default(char[]), StringSplitOptions.RemoveEmptyEntries))
            .ToLowerInvariant();
        return text.Length == 0 ? $"id:{hit.Id}" : text;
    }
}
