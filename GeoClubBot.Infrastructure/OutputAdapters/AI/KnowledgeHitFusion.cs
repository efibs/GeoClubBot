using System.Text.RegularExpressions;
using UseCases.OutputPorts.AI;

namespace Infrastructure.OutputAdapters.AI;

/// <param name="Hits">One search's results, best first.</param>
/// <param name="Weight">How much a place in this list counts, relative to the others.</param>
/// <param name="Name">Which search it was, e.g. "question → pixels", for anyone explaining a result.</param>
public sealed record WeightedHitList(IReadOnlyList<KnowledgeHit> Hits, double Weight, string Name = "");

/// <summary>
/// Merges the per-vector result lists into the excerpts offered to the model.
///
/// Reciprocal-rank fusion, as the store's own: positions are compared rather than scores, because
/// text-to-text similarity sits on a visibly higher scale than text-to-image. Done here rather than
/// in the store because the lists must count differently, and the store (Qdrant 1.15) cannot weight
/// them.
///
/// Excerpts with the same text are collapsed into one. The same sentence reaches the index several
/// times — two authors' copies of a region guide, a paragraph captioning each of the pictures it sits
/// beside — and replayed against the beta testers' questions, copies took 21 of 144 top-8 slots that
/// other guides could have filled. When the copies differ only in that one carries a picture, the
/// picture survives: the model still gets something it can attach.
///
/// Pure, so the arithmetic is tested rather than inferred from a store's behaviour.
/// </summary>
public static partial class KnowledgeHitFusion
{
    /// <summary>
    /// The store's own constant — its fusion matched this one exactly on production, ties aside — so
    /// weighting the lists is the only thing that changed when fusion moved here.
    /// </summary>
    public const int RankConstant = 2;

    public static IReadOnlyList<KnowledgeHit> Fuse(IReadOnlyList<WeightedHitList> lists, int limit)
    {
        ArgumentNullException.ThrowIfNull(lists);

        var scores = new Dictionary<Guid, double>();
        var hits = new Dictionary<Guid, KnowledgeHit>();
        var firstSeen = new Dictionary<Guid, int>();

        foreach (var list in lists)
        {
            for (var rank = 0; rank < list.Hits.Count; rank++)
            {
                var hit = list.Hits[rank];
                scores[hit.Id] = scores.GetValueOrDefault(hit.Id) + (list.Weight / (rank + RankConstant));

                // Ties are broken by first appearance, list order then rank, so the result is stable
                // and a text match wins a tie with a picture found by its pixels.
                if (hits.TryAdd(hit.Id, hit))
                {
                    firstSeen[hit.Id] = firstSeen.Count;
                }
            }
        }

        var fused = new List<KnowledgeHit>();
        var slotByText = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var id in scores.Keys.OrderByDescending(id => scores[id]).ThenBy(id => firstSeen[id]))
        {
            var hit = hits[id] with { Score = (float)scores[id] };
            var key = DedupeKey(hit);

            if (slotByText.TryGetValue(key, out var slot))
            {
                // A copy of something already offered. Only worth anything if it brings a picture the
                // kept copy lacks; then it takes the kept copy's place, keeping that copy's rank.
                if (fused[slot].ImageUrl is null && hit.ImageUrl is not null)
                {
                    fused[slot] = hit with { Score = fused[slot].Score };
                }

                continue;
            }

            if (fused.Count == limit)
            {
                continue;
            }

            slotByText[key] = fused.Count;
            fused.Add(hit);
        }

        return fused;
    }

    /// <summary>
    /// The text as a reader would compare it: case, emphasis, link targets and spacing ignored. An
    /// empty text never matches another, since nothing is known to be the same about the two.
    /// </summary>
    private static string DedupeKey(KnowledgeHit hit)
    {
        var text = MarkdownLink().Replace(hit.Text, "$1");
        text = Emphasis().Replace(text, " ");
        text = string.Join(' ', text.Split(default(char[]), StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();

        return text.Length == 0 ? $"id:{hit.Id}" : text;
    }

    [GeneratedRegex(@"\[([^\]]*)\]\([^)\s]*\)", RegexOptions.CultureInvariant)]
    private static partial Regex MarkdownLink();

    [GeneratedRegex(@"[*_`#>]+", RegexOptions.CultureInvariant)]
    private static partial Regex Emphasis();
}
