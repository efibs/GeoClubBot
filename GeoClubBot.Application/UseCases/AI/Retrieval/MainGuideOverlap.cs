using Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.OutputPorts.AI;
using UseCases.UseCases.AI.Ingestion;

namespace UseCases.UseCases.AI.Retrieval;

/// <summary>
/// Marks the excerpts that tell a clue PlonkIt tells too, for a question that asks for something new
/// (<see cref="LesserKnownQuestion"/>).
///
/// The model cannot know what PlonkIt carries: it is shown ten excerpts, not a guide. Asked for "a
/// clue I don't know yet" it answered with Sikkim's metal poles from one document and two Ukrainian
/// car metas from another, all three on PlonkIt, and was told so each time (2026-11-09). The index can
/// know: every excerpt has a text vector, and so has every PlonkIt tip. So each excerpt from another
/// guide is compared with PlonkIt's tips for its country, by the vector it already has — one batch of
/// reads, no embedding request — and one PlonkIt has a tip like is marked
/// (<see cref="KnowledgeHit.AlsoInMainGuide"/>).
///
/// Never fails an answer: if the comparison cannot be made, nothing is marked.
/// </summary>
public sealed partial class MainGuideOverlap(
    IKnowledgeIndex knowledgeIndex,
    KnowledgeCountryDirectory countryDirectory,
    IOptions<AiRetrievalConfiguration> retrievalConfiguration,
    ILogger<MainGuideOverlap> logger)
{
    /// <summary>The guide everybody has read.</summary>
    public const string MainGuide = SourceLinkClassifier.PlonkIt;

    /// <summary>
    /// The same excerpts, those PlonkIt has a tip like marked. PlonkIt's own, and excerpts that are no
    /// chunk of the index (a species range), come back as they were.
    /// </summary>
    public async Task<IReadOnlyList<KnowledgeHit>> MarkAsync(IReadOnlyList<KnowledgeHit> hits, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(hits);

        var settings = retrievalConfiguration.Value.LesserKnown;
        var candidates = hits
            .Where(hit => !hit.AlsoInMainGuide
                          && !string.IsNullOrWhiteSpace(hit.SourceType)
                          && !string.Equals(hit.SourceType, MainGuide, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (!settings.Enabled || candidates.Count == 0)
        {
            return hits;
        }

        try
        {
            // Guides spell one country differently, and PlonkIt's tips are found under any spelling of it.
            var gazetteer = await countryDirectory.ReadAsync(cancellationToken).ConfigureAwait(false);
            var spellings = candidates
                .Select(hit => hit.Country)
                .OfType<string>()
                .Where(country => country.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToDictionary(
                    country => country,
                    country => gazetteer.Resolve(country) is [var one] ? one.IndexNames : (IReadOnlyList<string>)[country],
                    StringComparer.Ordinal);

            var nearest = await knowledgeIndex
                .ReadNearestOfSourceAsync(candidates, MainGuide, spellings, cancellationToken)
                .ConfigureAwait(false);

            if (nearest is null || nearest.Count == 0)
            {
                return hits;
            }

            var marked = hits
                .Select(hit => nearest.TryGetValue(hit.Id, out var tip) && tip.Score >= settings.SameClueSimilarity
                    ? hit with { AlsoInMainGuide = true }
                    : hit)
                .ToList();

            LogMarked(logger, marked.Count(hit => hit.AlsoInMainGuide), candidates.Count, settings.SameClueSimilarity);
            return marked;
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LogFailed(logger, candidates.Count, exception);
            return hits;
        }
    }

    [LoggerMessage(LogLevel.Debug,
        "The question asks for something new: {Marked} of {Compared} excerpt(s) from other guides tell a clue PlonkIt has too (similarity ≥ {MinSimilarity}).")]
    static partial void LogMarked(ILogger logger, int marked, int compared, double minSimilarity);

    [LoggerMessage(LogLevel.Warning,
        "Comparing {Compared} excerpt(s) with PlonkIt's tips failed; none is marked as told there too.")]
    static partial void LogFailed(ILogger logger, int compared, Exception exception);
}
