using CsCheck;
using FluentAssertions;
using Infrastructure.OutputAdapters.AI;
using UseCases.OutputPorts.AI;
using Xunit;

namespace GeoClubBot.Tests.PropertyBased;

/// <summary>
/// Invariants of the fusion over arbitrary result lists: what reaches the model is bounded, never
/// repeats itself, comes only from what the store returned, and is ordered by the fused score.
/// Texts are drawn from a small pool so that copies, and copies carrying pictures, are common.
/// </summary>
public sealed class KnowledgeHitFusionPropertyTests
{
    private static readonly string[] Texts =
    [
        "Red soil is common.", "red  soil is COMMON.", "**Red soil** is common.", "Khasi pines grow here.",
        "Bollards are white.", "", "Gen 2 covers most of Japan."
    ];

    // Two guides, so copies from the same place and from elsewhere are both common.
    private static readonly Gen<KnowledgeHit> GenHit =
        Gen.Select(Gen.Int[0, Texts.Length - 1], Gen.Bool, Gen.Int[0, 5], (text, picture, id) => new KnowledgeHit(
            new Guid(id + 1, 0, 0, new byte[8]),
            0f,
            picture ? KnowledgeChunkKind.Image : KnowledgeChunkKind.Text,
            Texts[text],
            $"https://guide/{id % 2}",
            picture ? $"https://img/{id}.png" : null,
            null, null, null, null, 0));

    private static readonly Gen<List<WeightedHitList>> GenLists =
        Gen.Select(
                GenHit.List[0, 12].Select(hits => hits.DistinctBy(hit => hit.Id).ToList()),
                Gen.Double[0, 1],
                (hits, weight) => new WeightedHitList(hits, weight))
            .List[0, 3];

    [Fact]
    public void Never_returns_more_than_the_limit_or_anything_the_store_did_not() =>
        Gen.Select(GenLists, Gen.Int[1, 8]).Sample((lists, limit) =>
        {
            var fused = KnowledgeHitFusion.Fuse(lists, limit);
            var returned = lists.SelectMany(list => list.Hits).Select(hit => hit.Id).ToHashSet();

            fused.Should().HaveCountLessThanOrEqualTo(limit);
            fused.Should().OnlyContain(hit => returned.Contains(hit.Id));
            fused.Select(hit => hit.Id).Should().OnlyHaveUniqueItems();
        });

    [Fact]
    public void Is_ordered_by_fused_score() =>
        Gen.Select(GenLists, Gen.Int[1, 8]).Sample((lists, limit) =>
        {
            var scores = KnowledgeHitFusion.Fuse(lists, limit).Select(hit => hit.Score).ToList();

            scores.Should().BeInDescendingOrder();
        });

    [Fact]
    public void Loses_no_picture_or_id_of_a_copy_from_the_same_place() =>
        Gen.Select(GenLists, Gen.Int[1, 8]).Sample((lists, limit) =>
        {
            var returned = lists.SelectMany(list => list.Hits).DistinctBy(hit => hit.Id).ToList();

            foreach (var hit in KnowledgeHitFusion.Fuse(lists, limit))
            {
                // Text copies collapse wherever they come from; a picture only beside its own place's.
                var kept = returned
                    .Where(other => SameText(other, hit) && (other.ImageUrl is null || other.SourceUrl == hit.SourceUrl))
                    .ToList();
                var keptPictures = kept.Select(copy => copy.ImageUrl).OfType<string>().ToList();

                hit.ChunkIds.Should().BeEquivalentTo(kept.Select(copy => copy.Id), "every copy it stands for keeps its id");
                if (keptPictures.Count > 0)
                {
                    new[] { hit.ImageUrl }.Concat(hit.ExtraImageUrls).Should().Contain(keptPictures, "every same-place copy's picture is kept");
                }
            }
        });

    [Fact]
    public void Never_shows_a_picture_under_another_guides_link() =>
        Gen.Select(GenLists, Gen.Int[1, 8]).Sample((lists, limit) =>
        {
            var placeOf = lists.SelectMany(list => list.Hits)
                .Where(hit => hit.ImageUrl is not null)
                .GroupBy(hit => hit.ImageUrl!)
                .ToDictionary(group => group.Key, group => group.Select(hit => hit.SourceUrl).ToHashSet());

            foreach (var hit in KnowledgeHitFusion.Fuse(lists, limit).Where(hit => hit.ImageUrl is not null))
            {
                new[] { hit.ImageUrl! }.Concat(hit.ExtraImageUrls)
                    .Should().OnlyContain(picture => placeOf[picture].Contains(hit.SourceUrl));
            }
        });

    [Fact]
    public void Lists_each_picture_once_and_only_beside_a_picture() =>
        Gen.Select(GenLists, Gen.Int[1, 8]).Sample((lists, limit) =>
        {
            foreach (var hit in KnowledgeHitFusion.Fuse(lists, limit))
            {
                if (hit.ImageUrl is null)
                {
                    hit.ExtraImageUrls.Should().BeEmpty();
                    continue;
                }

                new[] { hit.ImageUrl }.Concat(hit.ExtraImageUrls).Should().OnlyHaveUniqueItems();
                hit.MergedChunkIds.Should().NotContain(hit.Id);
            }
        });

    [Fact]
    public void Offers_no_text_twice() =>
        Gen.Select(GenLists, Gen.Int[1, 8]).Sample((lists, limit) =>
        {
            var texts = KnowledgeHitFusion.Fuse(lists, limit)
                .Where(hit => hit.Text.Trim().Length > 0)
                .Select(hit => string.Join(' ', hit.Text.Replace("*", " ").Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant());

            texts.Should().OnlyHaveUniqueItems();
        });

    /// <summary>The same text as a reader would compare it — case, emphasis and spacing aside — and never empty.</summary>
    private static bool SameText(KnowledgeHit one, KnowledgeHit other) =>
        one.Id == other.Id || (Normalise(one.Text).Length > 0 && Normalise(one.Text) == Normalise(other.Text));

    private static string Normalise(string text) =>
        string.Join(' ', text.Replace("*", " ").Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
}
