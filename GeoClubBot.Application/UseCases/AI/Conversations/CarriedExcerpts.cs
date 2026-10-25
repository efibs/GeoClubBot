using UseCases.OutputPorts.AI;
using UseCases.UseCases.AI.Tools;

namespace UseCases.UseCases.AI.Conversations;

/// <param name="Hits">The excerpts to offer, the carried ones first.</param>
/// <param name="Carried">How many of <paramref name="Hits"/>, from the first, are carried from the previous answer.</param>
public sealed record FollowUpExcerpts(IReadOnlyList<KnowledgeHit> Hits, int Carried);

/// <summary>
/// The excerpts a follow-up is offered: the ones the answer it replies to cited, in the order that
/// answer numbered them, then what was retrieved for the follow-up itself.
///
/// The carried ones lead because they are what a follow-up most often points at — "that picture",
/// "source 3" — and numbered in their old order, a reader's "[3]" mostly still means [3]. What the new
/// search finds again is offered once, under its carried number, by the same rules a tool's results
/// are (<see cref="ExcerptLedger"/>): the same chunk, a copy merged into one, or the same text is a
/// duplicate, but a picture is new beside text it shares.
///
/// Pure, so the order and the de-duplication are tested without a store.
/// </summary>
public static class CarriedExcerpts
{
    public static FollowUpExcerpts Combine(
        IReadOnlyList<KnowledgeHit> carried,
        IReadOnlyList<KnowledgeHit> retrieved,
        int maxCarried)
    {
        ArgumentNullException.ThrowIfNull(carried);
        ArgumentNullException.ThrowIfNull(retrieved);

        // Nothing carried, nothing to de-duplicate against: the search's own list, exactly as it came.
        if (carried.Count == 0 || maxCarried <= 0)
        {
            return new FollowUpExcerpts(retrieved, 0);
        }

        var ledger = new ExcerptLedger([]);

        foreach (var hit in carried.Take(maxCarried))
        {
            ledger.Offer(hit);
        }

        var carriedCount = ledger.Count;

        foreach (var hit in retrieved)
        {
            ledger.Offer(hit);
        }

        return new FollowUpExcerpts(ledger.Hits, carriedCount);
    }
}
