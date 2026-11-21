using UseCases.OutputPorts.AI;
using UseCases.OutputPorts.Repositories;
using UseCases.UseCases.AI.Retrieval;

namespace UseCases.UseCases.AI.Quiz;

/// <summary>
/// Draws pictures to rate spread evenly over countries (#491): a random country first, then random
/// pictures within it. A plain random draw follows the index's own make-up, and one country's guide held
/// so many pictures that 55 drawn in a row were all from it; the quiz would have been mostly that country.
/// </summary>
public sealed class QuizPictureDraw(
    IKnowledgeIndex knowledgeIndex,
    IAiQuizItemRepository items,
    KnowledgeCountryDirectory countryDirectory,
    QuizPictureRater rater)
{
    /// <summary>Pictures drawn within one country to find one that is a clue and not rated yet.</summary>
    public const int PicturesPerCountry = 8;

    /// <summary>Empty draws in a row before giving up, however few countries there are: one draw may just be unlucky.</summary>
    private const int MinEmptyDraws = 3;

    /// <summary>
    /// Up to <paramref name="count"/> unrated, rateable pictures, each from another country while countries
    /// last, the countries in random order; then round again over them. Within <paramref name="countries"/>
    /// alone when given, as the spellings of one country.
    /// </summary>
    /// <param name="excludedSourceType">A guide family to leave out (PlonkIt, for a lesser-known quiz); null for none.</param>
    /// <param name="maxCountriesTried">How many countries may be tried in all, so a sparse index ends the search.</param>
    public async Task<IReadOnlyList<KnowledgeHit>> DrawUnratedAsync(
        int count,
        IReadOnlyList<string>? countries,
        string? excludedSourceType,
        int maxCountriesTried,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<IReadOnlyList<string>> groups;
        if (countries is not null)
        {
            groups = [countries];
        }
        else
        {
            var gazetteer = await countryDirectory.ReadAsync(cancellationToken).ConfigureAwait(false);
            var all = gazetteer.Countries.Select(mention => mention.IndexNames).ToArray();
            Random.Shared.Shuffle(all);
            groups = all;
        }

        if (groups.Count == 0)
        {
            return [];
        }

        var picked = new List<KnowledgeHit>(count);
        var seen = new HashSet<Guid>();
        var emptyInARow = 0;

        for (var tried = 0; picked.Count < count && tried < maxCountriesTried && emptyInARow < Math.Max(groups.Count, MinEmptyDraws); tried++)
        {
            var group = groups[tried % groups.Count];
            var drawn = new List<KnowledgeHit>();
            foreach (var spelling in group)
            {
                drawn.AddRange(await knowledgeIndex
                    .SampleAsync(new KnowledgeSample(PicturesPerCountry, KnowledgeChunkKind.Image, excludedSourceType, spelling), cancellationToken)
                    .ConfigureAwait(false));
            }

            var candidates = drawn.Where(QuizPictureRater.IsRateable).Where(hit => !seen.Contains(hit.Id)).DistinctBy(hit => hit.Id).ToList();
            var hashes = await items.ReadContentHashesAsync([.. candidates.Select(hit => hit.Id)], rater.Version, cancellationToken)
                .ConfigureAwait(false);
            var unrated = candidates.FirstOrDefault(hit => !hashes.TryGetValue(hit.Id, out var hash) || hash != QuizPictureRater.ContentHashOf(hit));

            if (unrated is null)
            {
                emptyInARow++;
                continue;
            }

            emptyInARow = 0;
            seen.Add(unrated.Id);
            picked.Add(unrated);
        }

        return picked;
    }
}
