using UseCases.OutputPorts.AI;

namespace UseCases.UseCases.AI.Tools;

/// <param name="Candidate">What Pl@ntNet made of the picture.</param>
/// <param name="LocalRecords">GBIF's records of it near the place; null when not counted.</param>
/// <param name="CountryRecords">GBIF's records of it in the whole country; null when not counted.</param>
/// <param name="Share">Its share of the weighed scores, 0 to 1; without a place, its image score.</param>
public sealed record WeighedCandidate(PlantCandidate Candidate, long? LocalRecords, long? CountryRecords, double Share)
{
    /// <summary>Whether GBIF has no record of it anywhere in the country: it most likely does not grow there.</summary>
    public bool IsUnrecordedInCountry => CountryRecords == 0;
}

/// <param name="Candidates">Best first: by the weighed score when the place's records were read, else by the picture alone.</param>
/// <param name="Place">Where the member said the screenshot was taken; null when they did not say.</param>
/// <param name="IsWeighed">Whether the place's records were read and re-ordered the candidates.</param>
public sealed record PlantWeighing(IReadOnlyList<WeighedCandidate> Candidates, ScreenshotPlace? Place, bool IsWeighed)
{
    public WeighedCandidate Best => Candidates[0];

    /// <summary>
    /// Whether the best candidate is sure enough to be named as the plant. Without the place's records,
    /// its image score has to reach <paramref name="minConfidence"/>. With them, its share of the weighed
    /// scores does, and its image score has to reach <paramref name="minImageScore"/>: the records can lift
    /// a candidate the picture barely supports to the top, and that is a likelihood, not an identification.
    /// </summary>
    public bool IsIdentified(double minConfidence, double minImageScore) =>
        IsWeighed
            ? Best.Share >= minConfidence && Best.Candidate.Score >= minImageScore
            : Best.Candidate.Score >= minConfidence;
}

/// <summary>
/// Weighs Pl@ntNet's candidates by what is recorded where the screenshot was taken: each image score
/// times (records near the place + 1) to <c>RecordsWeight</c>, and times <c>UnrecordedFactor</c> when
/// the country has no record of the plant at all.
///
/// Pl@ntNet scores what the picture shows, over a world flora learned mostly from European
/// photographs. The records say what grows there: on a hazy frame near Boston every candidate scored
/// about 1 %, and the 7,269 records of eastern white pine within 50 km against 209 of American elm are
/// what tell them apart; a red pine topping a Bulgarian screenshot has no Bulgarian record at all
/// (2026-11-15). The power keeps a clear picture in charge: a 52 % Norway spruce near Boston stays first
/// over trees recorded far more often there.
///
/// Pure, so the arithmetic is pinned by a test.
/// </summary>
public static class PlantLocationWeighting
{
    /// <summary>
    /// The candidates weighed by <paramref name="localRecords"/> (near the place) and
    /// <paramref name="countryRecords"/> (in its country), keyed by GBIF taxon key; Pl@ntNet's order
    /// when there is no place, or its records could not be read. A candidate without a GBIF key is
    /// weighed as recorded once nowhere: its picture alone speaks for it.
    /// </summary>
    public static PlantWeighing Weigh(
        PlantIdentification identification,
        ScreenshotPlace? place,
        IReadOnlyDictionary<long, long>? localRecords,
        IReadOnlyDictionary<long, long>? countryRecords,
        double recordsWeight,
        double unrecordedFactor)
    {
        ArgumentNullException.ThrowIfNull(identification);

        if (place is null || localRecords is null)
        {
            return new PlantWeighing(
                [.. identification.Candidates.Select(candidate => new WeighedCandidate(candidate, null, null, candidate.Score))],
                place,
                IsWeighed: false);
        }

        var weighed = identification.Candidates
            .Select(candidate =>
            {
                long? local = candidate.GbifTaxonKey is { } key ? localRecords.GetValueOrDefault(key) : null;
                long? country = candidate.GbifTaxonKey is { } countryKey && countryRecords is not null
                    ? countryRecords.GetValueOrDefault(countryKey)
                    : null;

                var weight = Math.Pow((local ?? 0) + 1, recordsWeight) * (country == 0 ? unrecordedFactor : 1);
                return (Candidate: candidate, Local: local, Country: country, Weighed: candidate.Score * weight);
            })
            .OrderByDescending(entry => entry.Weighed)
            .ThenByDescending(entry => entry.Candidate.Score)
            .ToList();

        var total = weighed.Sum(entry => entry.Weighed);

        return new PlantWeighing(
            [.. weighed.Select(entry => new WeighedCandidate(entry.Candidate, entry.Local, entry.Country, total > 0 ? entry.Weighed / total : 0))],
            place,
            IsWeighed: true);
    }
}
