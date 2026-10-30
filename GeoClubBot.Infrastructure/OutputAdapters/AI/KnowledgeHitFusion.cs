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
/// other guides could have filled. The copies' pictures are not collapsed with them: the kept excerpt
/// carries every one (<see cref="KnowledgeHit.ExtraImageUrls"/>), and the copies' ids. A Google Slides
/// caption sits beside each picture of its slide — eight of them on "India Poles > Slide 18" — and
/// keeping only the first had left 3,389 of the index's 4,738 slide pictures (72 %) impossible to offer.
///
/// Only a copy from the same place adds its picture: the same link and the same section, as the
/// pictures of one slide are. Matching text proves nothing more than that — two decks' slides captioned
/// only "Bollards" — and an extra picture is shown under the kept excerpt's title, link and number, so a
/// Czech bollard would be presented as the Polish one. A picture from elsewhere is left out, as every
/// copy's picture was before extra pictures were kept, and so is its id: the excerpt does not stand for it.
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

    /// <param name="headingOnlyPictureWeight">
    /// Multiplies the score of a picture whose only text is a heading
    /// (<see cref="KnowledgeHit.IsHeadingOnlyPicture"/>): the model can say nothing about one, yet a
    /// heading like "Distribution" matches many a question. One leaves them where they rank.
    /// </param>
    public static IReadOnlyList<KnowledgeHit> Fuse(
        IReadOnlyList<WeightedHitList> lists,
        int limit,
        double headingOnlyPictureWeight = 1d)
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

        if (headingOnlyPictureWeight < 1d)
        {
            foreach (var (id, hit) in hits)
            {
                if (hit.IsHeadingOnlyPicture)
                {
                    scores[id] *= Math.Max(0d, headingOnlyPictureWeight);
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
                // A copy of something already offered: one excerpt, in the place the better copy
                // earned, and every picture either of them carries.
                fused[slot] = Merge(fused[slot], hit);
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
    /// <paramref name="kept"/> with <paramref name="copy"/> folded in. A copy bringing the first picture
    /// takes the kept copy's place, so the excerpt is one the model can attach — it is then shown under
    /// the copy's own title and link. A copy from the same place bringing another picture adds it; one
    /// from elsewhere is dropped (<see cref="IsSamePlace"/>). The excerpt keeps the better copy's score,
    /// and the id of every copy it stands for.
    /// </summary>
    private static KnowledgeHit Merge(KnowledgeHit kept, KnowledgeHit copy)
    {
        if (kept.ImageUrl is not null && copy.ImageUrl is not null && !IsSamePlace(kept, copy))
        {
            return kept;
        }

        var ids = kept.ChunkIds.Concat(copy.ChunkIds).Distinct().ToList();

        if (kept.ImageUrl is null)
        {
            var lead = copy.ImageUrl is null ? kept : copy with { Score = kept.Score };
            return lead with { MergedChunkIds = [.. ids.Where(id => id != lead.Id)] };
        }

        var pictures = new[] { kept.ImageUrl }
            .Concat(kept.ExtraImageUrls)
            .Concat(copy.ImageUrl is null ? [] : [copy.ImageUrl, .. copy.ExtraImageUrls])
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return kept with
        {
            ExtraImageUrls = [.. pictures.Skip(1)],
            MergedChunkIds = [.. ids.Where(id => id != kept.Id)]
        };
    }

    /// <summary>
    /// Whether two chunks come from the same place in the same guide: one link and one section, as the
    /// pictures beside one slide's caption do. Only then is one's picture shown as the other's.
    /// </summary>
    private static bool IsSamePlace(KnowledgeHit one, KnowledgeHit other) =>
        string.Equals(one.SourceUrl, other.SourceUrl, StringComparison.Ordinal)
        && string.Equals(one.SectionPath, other.SectionPath, StringComparison.Ordinal);

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
