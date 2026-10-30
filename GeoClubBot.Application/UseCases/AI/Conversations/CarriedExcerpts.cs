using UseCases.OutputPorts.AI;
using UseCases.UseCases.AI.Tools;

namespace UseCases.UseCases.AI.Conversations;

/// <param name="Hits">The excerpts to offer, the carried ones first.</param>
/// <param name="Carried">How many of <paramref name="Hits"/>, from the first, are carried from the previous answer.</param>
/// <param name="CarriedSourceNumbers">
/// For each carried excerpt, the number the reader saw it listed under beneath the previous answer —
/// or null when those are simply 1 to <paramref name="Carried"/>, or cannot be told.
/// </param>
public sealed record FollowUpExcerpts(IReadOnlyList<KnowledgeHit> Hits, int Carried, IReadOnlyList<int>? CarriedSourceNumbers = null);

/// <summary>
/// The excerpts a follow-up is offered: the ones the answer it replies to cited, in the order that
/// answer numbered them, then what was retrieved for the follow-up itself.
///
/// The carried ones lead because they are what a follow-up most often points at — "that picture",
/// "source 3". What the new search finds again is offered once, under its carried number, by the same
/// rules a tool's results are (<see cref="ExcerptLedger"/>): the same chunk, a copy merged into one, or
/// the same text is a duplicate, but a picture is new beside text it shares.
///
/// A reader's numbers are not always the carried ones. The reader saw every text excerpt of one section
/// of a page under one number (<see cref="CitationResolver.KeyOf"/>), so an answer citing two chunks of
/// a Google Doc section, then a picture, listed the picture as [2] while it is carried as [3]. The
/// numbers the reader saw are worked out the same way from the excerpts read back, and when they differ
/// from the carried ones each carried excerpt says which it was
/// (<see cref="FollowUpExcerpts.CarriedSourceNumbers"/>). When a cited chunk did not come back, the
/// numbering cannot be trusted, and nothing is said.
///
/// Worked out by today's rule, which is not always the one the answer was posted under: an answer from
/// before sections were numbered apart (2026-10) gave a whole page one number, so a follow-up to it can
/// be told numbers its reader did not see. Accepted rather than dated, because a follow-up replays only
/// an answer under <c>AI:Conversation:MaxIdleHours</c> (24) old, so the mismatch ends a day after the
/// deploy; and it misleads only a follow-up that names a number.
///
/// Pure, so the order, the numbering and the de-duplication are tested without a store.
/// </summary>
public static class CarriedExcerpts
{
    /// <param name="citedChunkIds">
    /// The chunk ids the previous answer cited, as asked of the index; the numbers the reader saw are
    /// only given when every one of them came back. Null leaves them out.
    /// </param>
    public static FollowUpExcerpts Combine(
        IReadOnlyList<KnowledgeHit> carried,
        IReadOnlyList<KnowledgeHit> retrieved,
        int maxCarried,
        IReadOnlyList<Guid>? citedChunkIds = null)
    {
        ArgumentNullException.ThrowIfNull(carried);
        ArgumentNullException.ThrowIfNull(retrieved);

        // Nothing carried, nothing to de-duplicate against: the search's own list, exactly as it came.
        if (carried.Count == 0 || maxCarried <= 0)
        {
            return new FollowUpExcerpts(retrieved, 0);
        }

        var readerNumbers = ReaderNumbers(carried, citedChunkIds);
        var ledger = new ExcerptLedger([]);
        var carriedNumbers = new List<int>();

        for (var index = 0; index < carried.Count && index < maxCarried; index++)
        {
            if (ledger.Offer(carried[index]) is not null && readerNumbers is not null)
            {
                carriedNumbers.Add(readerNumbers[index]);
            }
        }

        var carriedCount = ledger.Count;

        foreach (var hit in retrieved)
        {
            ledger.Offer(hit);
        }

        // Said only where it differs: an excerpt carried as [2] that the reader saw as [2] needs no note.
        var differs = readerNumbers is not null
                      && carriedNumbers.Where((number, index) => number != index + 1).Any();

        return new FollowUpExcerpts(ledger.Hits, carriedCount, differs ? carriedNumbers : null);
    }

    /// <summary>
    /// The number the reader saw each carried excerpt under, or null when a cited chunk is missing.
    /// The excerpts come back in the order the previous answer numbered them, so numbering their reader
    /// keys in order of first appearance gives back the reader's numbers.
    /// </summary>
    private static List<int>? ReaderNumbers(IReadOnlyList<KnowledgeHit> carried, IReadOnlyList<Guid>? citedChunkIds)
    {
        if (citedChunkIds is null)
        {
            return null;
        }

        var returned = carried.SelectMany(hit => hit.ChunkIds).ToHashSet();
        if (!citedChunkIds.Where(id => id != Guid.Empty).All(returned.Contains))
        {
            return null;
        }

        var numbering = new Dictionary<string, int>(StringComparer.Ordinal);
        var numbers = new List<int>(carried.Count);

        foreach (var hit in carried)
        {
            AiPromptBuilder.FormatExcerpt(hit, marker: 0, out var excerpt);

            var key = CitationResolver.KeyOf(excerpt);
            if (!numbering.TryGetValue(key, out var number))
            {
                number = numbering.Count + 1;
                numbering[key] = number;
            }

            numbers.Add(number);
        }

        return numbers;
    }
}
