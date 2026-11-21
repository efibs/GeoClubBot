using UseCases.OutputPorts.AI;
using UseCases.UseCases.AI.StreetView;

namespace UseCases.UseCases.AI.Review;

/// <summary>
/// Turns what each round was seen to show into the excerpts a review offers, and the numbers each round
/// refers to them by. Pure.
///
/// A round offers the clues it was seen to show, the country's before the region's, and only then what
/// to look for next time: for a round with the wrong country that showed no country-wide clue, the
/// country's best; for one in the right country that showed nothing of its region, the region's best;
/// for a round no model could look at, the country's best, since nothing is known of it. A well-played
/// round that showed nothing is offered nothing. The rounds take turns, so a long game offers something
/// for each round before more for any, and an excerpt two rounds share is offered once, under one number.
/// </summary>
public static class ReviewExcerpts
{
    /// <summary>Clues seen in one round offered at most: more makes a long review longer, not better.</summary>
    public const int MaxSeenPerRound = 6;

    /// <param name="rounds">The rounds, as read.</param>
    /// <param name="clues">The guides' clues for each round, in the rounds' order (<see cref="RoundClueFinder"/>).</param>
    /// <param name="sights">What each round's panorama was seen to show, in the rounds' order; null for a round not looked at.</param>
    /// <param name="wellPlayedPoints">Points from which a round in the right country needs nothing to look for.</param>
    /// <param name="unseenForMissed">Clues to look for offered for a round with the wrong country that showed none.</param>
    /// <param name="maxExcerpts">Excerpts offered in all.</param>
    /// <returns>The excerpts in the order they are numbered (from one), and the rounds with what they showed.</returns>
    public static (IReadOnlyList<KnowledgeHit> Hits, IReadOnlyList<ReviewedRound> Rounds) Assemble(
        IReadOnlyList<ReviewedRound> rounds,
        IReadOnlyList<IReadOnlyList<RoundClue>> clues,
        IReadOnlyList<PlaceSight?> sights,
        int wellPlayedPoints,
        int unseenForMissed,
        int maxExcerpts)
    {
        ArgumentNullException.ThrowIfNull(rounds);
        ArgumentNullException.ThrowIfNull(clues);
        ArgumentNullException.ThrowIfNull(sights);

        var plans = rounds.Select((round, index) => Plan(
                round,
                index < clues.Count ? clues[index] : [],
                index < sights.Count ? sights[index] : null,
                wellPlayedPoints,
                unseenForMissed))
            .ToList();

        // Numbered taking turns, each round's own order kept.
        var hits = new List<KnowledgeHit>();
        var markers = new Dictionary<Guid, int>();
        for (var rank = 0; plans.Any(plan => rank < plan.Entries.Count); rank++)
        {
            foreach (var plan in plans)
            {
                if (rank >= plan.Entries.Count)
                {
                    continue;
                }

                var hit = plan.Entries[rank].Clue.Hit;
                if (!markers.ContainsKey(hit.Id) && hits.Count < maxExcerpts)
                {
                    hits.Add(hit);
                    markers[hit.Id] = hits.Count;
                }
            }
        }

        var reviewed = rounds.Select((round, index) =>
        {
            var plan = plans[index];
            if (!plan.Checked && plan.Entries.Count == 0)
            {
                return round with { Evidence = new RoundEvidence(false, [], [], []) };
            }

            var seen = plan.Entries
                .Where(entry => entry.Where is not null && markers.ContainsKey(entry.Clue.Hit.Id))
                .Select(entry => new SeenExcerpt(markers[entry.Clue.Hit.Id], entry.Where!, entry.Clue.IsRegional))
                .ToList();
            var toLookFor = plan.Entries
                .Where(entry => entry.Where is null && markers.ContainsKey(entry.Clue.Hit.Id))
                .Select(entry => markers[entry.Clue.Hit.Id])
                .ToList();

            return round with { Evidence = new RoundEvidence(plan.Checked, seen, toLookFor, plan.AlsoSeen) };
        }).ToList();

        return (hits, reviewed);
    }

    private static RoundPlan Plan(
        ReviewedRound round,
        IReadOnlyList<RoundClue> clues,
        PlaceSight? sight,
        int wellPlayedPoints,
        int unseenForMissed)
    {
        var verdict = sight is { Checked: true } ? sight.Verdict : null;
        var wellPlayed = round.Played && !round.MissedCountry && round.Points >= wellPlayedPoints;

        var seen = verdict is null
            ? []
            : verdict.Seen
                .Where(clue => clue.Index >= 0 && clue.Index < clues.Count)
                .Select(clue => new Entry(clues[clue.Index], clue.Where))
                .OrderBy(entry => entry.Clue.IsRegional)
                .Take(MaxSeenPerRound)
                .ToList();

        var seenIds = seen.Select(entry => entry.Clue.Hit.Id).ToHashSet();
        var unseen = clues.Where(clue => !seenIds.Contains(clue.Hit.Id)).ToList();
        var countryWide = unseen.Where(clue => !clue.IsRegional);
        var regional = unseen.Where(clue => clue.IsRegional);

        IEnumerable<RoundClue> toLookFor;
        if (verdict is null)
        {
            // Nothing is known of what it showed: the country's best, unless it went well anyway.
            toLookFor = wellPlayed ? [] : countryWide.Take(Math.Max(1, unseenForMissed));
        }
        else if (!round.Played || round.MissedCountry)
        {
            toLookFor = seen.Any(entry => !entry.Clue.IsRegional) ? [] : countryWide.Take(unseenForMissed);
        }
        else
        {
            toLookFor = wellPlayed || seen.Any(entry => entry.Clue.IsRegional) ? [] : regional.Take(1);
        }

        return new RoundPlan(
            verdict is not null,
            [.. seen, .. toLookFor.Select(clue => new Entry(clue, null))],
            verdict?.AlsoSeen ?? []);
    }

    /// <param name="Where">Where it was seen; null for a clue to look for.</param>
    private sealed record Entry(RoundClue Clue, string? Where);

    private sealed record RoundPlan(bool Checked, IReadOnlyList<Entry> Entries, IReadOnlyList<string> AlsoSeen);
}
