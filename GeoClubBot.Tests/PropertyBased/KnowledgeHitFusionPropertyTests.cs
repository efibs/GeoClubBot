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

    private static readonly Gen<KnowledgeHit> GenHit =
        Gen.Select(Gen.Int[0, Texts.Length - 1], Gen.Bool, Gen.Int[0, 5], (text, picture, id) => new KnowledgeHit(
            new Guid(id + 1, 0, 0, new byte[8]),
            0f,
            picture ? KnowledgeChunkKind.Image : KnowledgeChunkKind.Text,
            Texts[text],
            $"https://guide/{id}",
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
    public void Offers_no_text_twice() =>
        Gen.Select(GenLists, Gen.Int[1, 8]).Sample((lists, limit) =>
        {
            var texts = KnowledgeHitFusion.Fuse(lists, limit)
                .Where(hit => hit.Text.Trim().Length > 0)
                .Select(hit => string.Join(' ', hit.Text.Replace("*", " ").Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant());

            texts.Should().OnlyHaveUniqueItems();
        });
}
