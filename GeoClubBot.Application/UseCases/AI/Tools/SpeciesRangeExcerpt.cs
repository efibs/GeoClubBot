using System.Globalization;
using System.Text;
using UseCases.OutputPorts.AI;
using UseCases.UseCases.AI.Retrieval;
using Utilities;

namespace UseCases.UseCases.AI.Tools;

/// <summary>
/// Turns a species' record counts into one excerpt the model can cite, numbered and listed under the
/// answer like a guide's, with the database's page as its source.
///
/// The counts come with their caveat in the excerpt itself, because a model reads "Philippines 256,
/// Thailand 87" as "more common in the Philippines" unless told otherwise. They count what people
/// recorded: they follow how many people look in a place, and they include herbarium specimens and
/// plantations, so few records do not make a plant rare there and many do not make it wild.
///
/// Pure, so the wording is pinned by a test.
/// </summary>
public static class SpeciesRangeExcerpt
{
    /// <summary>The source's title, which the reply's source list names it by.</summary>
    public const string SourceTitle = "GBIF";

    private const string Caveat =
        "These count occurrence records, not plants: they follow how much people observe and collect in a place, "
        + "and include herbarium specimens, gardens and plantations, so few records do not make the plant rare "
        + "there and many do not make it wild.";

    /// <summary>The species' public page, cited as the excerpt's source.</summary>
    public static string SourceUrlOf(SpeciesRange range) =>
        $"https://www.gbif.org/species/{range.TaxonKey.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>
    /// Said of the map when there is one, so the model knows what the excerpt's picture is. A
    /// description, not an instruction: "cite this excerpt to show where the plant is found" came back
    /// in an answer as "citing it will attach the map to this reply" (replay of 2026-11-11). That
    /// pictures are shown by citing them is the system prompt's to say.
    /// </summary>
    private const string MapNote = "The picture is a map of these records.";

    /// <summary>
    /// The same of a map framed on one country, which shows the neighbours' records within the frame
    /// too: said so the model does not read a state's worth of records into what lies across a border.
    /// </summary>
    private static string CountryMapNote(string country) =>
        $"The picture is a map of its records in and around {country}; the counts above are for {country} alone.";

    /// <param name="askedName">The name the model asked about, to say how it was matched.</param>
    /// <param name="maxAreas">Most countries or regions listed, most records first.</param>
    /// <param name="mapUrl">
    /// The link of a map of the records (<see cref="ISpeciesRangeMap"/>), or null. With one the excerpt
    /// comes with a picture, attached like any guide's when the answer cites it.
    /// </param>
    public static KnowledgeHit ToHit(SpeciesRange range, string askedName, int maxAreas, string? mapUrl = null)
    {
        ArgumentNullException.ThrowIfNull(range);

        var name = string.IsNullOrWhiteSpace(range.CanonicalName) ? range.ScientificName : range.CanonicalName;
        var country = range.CountryCode is { } code ? IsoCountries.NameOf(code) : null;
        var heading = country is null ? $"Where {name} is recorded (GBIF)" : $"Where {name} is recorded in {country} (GBIF)";

        var text = new StringBuilder();
        text.Append(range.ScientificName).Append(" (").Append(range.Rank.ToLowerInvariant()).Append(").");
        if (range.MatchedByCommonName)
        {
            text.Append(" Found by its common name \"").Append(askedName).Append("\".");
        }
        else if (range.FuzzyMatch)
        {
            text.Append(" The name \"").Append(askedName).Append("\" was read as this one.");
        }

        text.AppendLine();

        var areas = Areas(range).Take(Math.Max(1, maxAreas)).ToList();
        if (areas.Count == 0)
        {
            text.Append(country is null
                ? "GBIF holds no occurrence records of it."
                : $"GBIF holds no occurrence records of it in {country}.");
        }
        else
        {
            text.Append(country is null
                ? "Occurrence records by country: "
                : $"Occurrence records in {country} by state or province: ");
            text.Append(string.Join(", ", areas.Select(area =>
                $"{area.Name} {area.Records.ToString("N0", CultureInfo.InvariantCulture)}")));
            text.Append(" (").Append(range.TotalRecords.ToString("N0", CultureInfo.InvariantCulture))
                .Append(country is null ? " records in all)." : $" records in {country} in all).");
            text.AppendLine();
            text.Append(Caveat);

            if (!string.IsNullOrWhiteSpace(mapUrl))
            {
                text.AppendLine();
                text.Append(country is null ? MapNote : CountryMapNote(country));
            }
        }

        var hasMap = areas.Count > 0 && !string.IsNullOrWhiteSpace(mapUrl);

        return new KnowledgeHit(
            // The same species and scope is the same excerpt, so asking twice offers it once.
            DeterministicGuid.FromName("gbif", $"{range.TaxonKey}|{range.CountryCode}"),
            Score: 0f,
            hasMap ? KnowledgeChunkKind.Image : KnowledgeChunkKind.Text,
            text.ToString(),
            SourceUrlOf(range),
            ImageUrl: hasMap ? mapUrl : null,
            SourceTitle,
            Country: null,
            SectionPath: heading,
            Author: null,
            Priority: 0);
    }

    /// <summary>
    /// Areas named for a reader, most records first. Country codes become names and the database's
    /// "unknown" is dropped. Regions are free text in the records, so one region spelt two ways —
    /// "Querétaro" and "Queretaro", "San Luis Potosí" and "San luis potosi" for Yucca filifera — is
    /// counted once, under the spelling with more records.
    /// </summary>
    private static IEnumerable<SpeciesRangeArea> Areas(SpeciesRange range)
    {
        if (range.CountryCode is null)
        {
            return range.Areas
                .Where(area => IsoCountries.IsCountry(area.Name))
                .Select(area => new SpeciesRangeArea(IsoCountries.NameOf(area.Name), area.Records))
                .OrderByDescending(area => area.Records);
        }

        return range.Areas
            .Where(area => !string.IsNullOrWhiteSpace(area.Name))
            .GroupBy(area => CountryGazetteer.Normalise(area.Name), StringComparer.Ordinal)
            .Select(group => new SpeciesRangeArea(
                group.OrderByDescending(area => area.Records).First().Name.Trim(),
                group.Sum(area => area.Records)))
            .OrderByDescending(area => area.Records);
    }
}
