using Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.OutputPorts.AI;
using UseCases.UseCases.AI.Retrieval;
using UseCases.UseCases.AI.Tools;

namespace UseCases.UseCases.AI.Review;

/// <summary>A clue the guides give for a round's place, and which search found it.</summary>
public sealed record RoundClue(KnowledgeHit Hit, RoundSearchKind Kind)
{
    public bool IsRegional => Kind == RoundSearchKind.Region;
}

/// <summary>
/// The guides' clues for each round's place: the clues the round's panorama is checked against.
/// Every round's searches (<see cref="RoundReviewPrompt.Searches"/>) are embedded together, then each
/// is searched within the round's country, under every spelling the index files it under. A round's
/// lists are merged taking turns, so the country's reliable clues, its roads, its poles, the contrast
/// with the guess and the region each get a place before any gets a second.
/// </summary>
public sealed partial class RoundClueFinder(
    IEmbedder embedder,
    IKnowledgeIndex knowledgeIndex,
    KnowledgeCountryDirectory countryDirectory,
    IOptions<AiConfiguration> aiConfiguration,
    IOptions<AiRoundReviewConfiguration> configuration,
    ILogger<RoundClueFinder> logger)
{
    /// <summary>The embedding requests the rounds' searches take: one per batch the embedder sends.</summary>
    public int EmbeddingRequests(IReadOnlyList<ReviewedRound> rounds)
    {
        ArgumentNullException.ThrowIfNull(rounds);

        var searches = rounds.Where(round => round.CountryCode is not null).Sum(round => RoundReviewPrompt.Searches(round).Count);
        var batch = Math.Max(1, aiConfiguration.Value.OpenRouter.EmbeddingBatchSize);
        return (searches + batch - 1) / batch;
    }

    /// <summary>The clues for each round, in the rounds' order; empty for a round that was not placed, or when the embedding failed.</summary>
    public async Task<IReadOnlyList<IReadOnlyList<RoundClue>>> FindAsync(IReadOnlyList<ReviewedRound> rounds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rounds);

        var clues = rounds.Select(_ => (IReadOnlyList<RoundClue>)[]).ToArray();
        var searches = rounds
            .Select((round, index) => (Index: index, Round: round, Searches: round.CountryCode is null ? [] : RoundReviewPrompt.Searches(round)))
            .Where(entry => entry.Searches.Count > 0)
            .ToList();
        if (searches.Count == 0)
        {
            return clues;
        }

        var texts = searches.SelectMany(entry => entry.Searches.Select(search => (EmbeddingInput)new TextEmbeddingInput(search.Text))).ToList();
        var embedded = await embedder.EmbedAsync(texts, cancellationToken).ConfigureAwait(false);
        if (embedded.IsFailure || embedded.Value.Count != texts.Count)
        {
            LogEmbeddingFailed(logger, embedded.IsFailure ? embedded.Error.Message : "the embedder returned the wrong number of vectors");
            return clues;
        }

        var settings = configuration.Value.SightCheck;
        var gazetteer = await countryDirectory.ReadAsync(cancellationToken).ConfigureAwait(false);
        var vector = 0;
        foreach (var (index, round, roundSearches) in searches)
        {
            var spellings = gazetteer.Resolve(IsoCountries.NameOf(round.CountryCode!)) is { Count: 1 } mentions
                ? mentions[0].IndexNames
                : [IsoCountries.NameOf(round.CountryCode!).ToLowerInvariant()];

            var lists = new List<(RoundSearchKind Kind, IReadOnlyList<KnowledgeHit> Hits)>();
            foreach (var search in roundSearches)
            {
                var found = await SearchAsync(embedded.Value[vector++], spellings, settings.ExcerptsPerSearch, cancellationToken).ConfigureAwait(false);
                lists.Add((search.Kind, found));
            }

            clues[index] = Merge(lists, settings.CluesPerRound);
        }

        return clues;
    }

    /// <summary>The lists taken in turns, none twice, at most <paramref name="limit"/>.</summary>
    public static IReadOnlyList<RoundClue> Merge(IReadOnlyList<(RoundSearchKind Kind, IReadOnlyList<KnowledgeHit> Hits)> lists, int limit)
    {
        ArgumentNullException.ThrowIfNull(lists);

        var merged = new List<RoundClue>();
        var seen = new HashSet<Guid>();
        for (var rank = 0; merged.Count < limit && lists.Any(list => rank < list.Hits.Count); rank++)
        {
            foreach (var (kind, hits) in lists)
            {
                if (rank < hits.Count && merged.Count < limit && seen.Add(hits[rank].Id))
                {
                    merged.Add(new RoundClue(hits[rank], kind));
                }
            }
        }

        return merged;
    }

    private async Task<IReadOnlyList<KnowledgeHit>> SearchAsync(
        ReadOnlyMemory<float> vector,
        IReadOnlyList<string> spellings,
        int limit,
        CancellationToken cancellationToken)
    {
        foreach (var spelling in spellings)
        {
            var found = await knowledgeIndex.SearchAsync(
                    new KnowledgeQuery { TextVector = vector, Country = spelling, Limit = limit },
                    cancellationToken)
                .ConfigureAwait(false);
            if (found.Count > 0)
            {
                return found;
            }
        }

        return [];
    }

    [LoggerMessage(LogLevel.Warning, "The rounds' searches could not be embedded ({Reason}); the rounds are looked at without the guides' clues.")]
    static partial void LogEmbeddingFailed(ILogger logger, string reason);
}
