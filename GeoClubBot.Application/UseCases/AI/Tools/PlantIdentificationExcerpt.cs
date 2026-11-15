using System.Globalization;
using System.Text;
using UseCases.OutputPorts.AI;
using Utilities;

namespace UseCases.UseCases.AI.Tools;

/// <summary>
/// What Pl@ntNet made of the screenshot — weighed by where it was taken, when the member said — as one
/// excerpt among the guides', so the model names the plant from it instead of from a look-alike guide
/// picture. Pure, so the wording is pinned by a test.
/// </summary>
public static class PlantIdentificationExcerpt
{
    /// <summary>The source's title, which the reply's source list names it by: the attribution Pl@ntNet asks for.</summary>
    public const string SourceTitle = "Pl@ntNet";

    /// <summary>Where the source line leads: the service, since the identification itself has no page.</summary>
    public const string SourceUrl = "https://identify.plantnet.org/";

    public const string Heading = "What plant the screenshot shows (Pl@ntNet)";

    /// <summary>Said when the service saw nothing it knows, so the model does not guess with confidence.</summary>
    public const string NotRecognisedText =
        "Pl@ntNet, an image-based plant identification service, found no plant it recognises in the screenshot. "
        + "If the question is about a plant, say that it could not be identified rather than naming one; "
        + "a look-alike in a guide is only a look-alike.";

    /// <summary>
    /// The candidates with their scores, and a verdict: the best one when it is sure enough (see
    /// <see cref="PlantWeighing.IsIdentified"/>), the likeliest of an uncertain lot otherwise. When the
    /// member said where the screenshot was taken, each candidate comes with its records there, and a
    /// best image match never recorded in the country is named as unlikely. The genus is given when
    /// Pl@ntNet is sure of it: on a Street View frame it often is when the species is a toss-up.
    /// </summary>
    public static KnowledgeHit ToHit(
        PlantIdentification identification,
        PlantWeighing weighing,
        double minConfidence,
        double minImageScore,
        int maxCandidates,
        double radiusKm)
    {
        ArgumentNullException.ThrowIfNull(identification);
        ArgumentNullException.ThrowIfNull(weighing);

        var shown = weighing.Candidates.Take(Math.Max(1, maxCandidates)).ToList();
        var best = shown[0];
        var identified = weighing.IsIdentified(minConfidence, minImageScore);
        var place = weighing.Place;
        var area = place?.DescribeArea(radiusKm);

        var text = new StringBuilder("Pl@ntNet, an image-based plant identification service, ");
        if (identified)
        {
            text.Append("identifies the plant in the screenshot as ").Append(Describe(best.Candidate))
                .Append(", ").Append(Percent(best.Candidate.Score)).Append(" from the picture.");
        }
        else if (weighing.IsWeighed)
        {
            text.Append("cannot tell from the screenshot alone which plant it is: its best match from the picture scores ")
                .Append(Percent(identification.Best.Score)).Append('.');
        }
        else
        {
            text.Append("is not sure what the plant in the screenshot is; its best guess is ").Append(Describe(best.Candidate))
                .Append(" at only ").Append(Percent(best.Candidate.Score)).Append(" confidence, so say it is a guess.");
        }

        if (identification.Genera.FirstOrDefault() is { } genus && genus.Score >= minConfidence)
        {
            text.Append(identified ? " Its genus: " : " Its genus is clearer: ")
                .Append(DescribeGenus(genus)).Append(", ").Append(Percent(genus.Score)).Append('.');
        }

        if (place is not null && weighing.IsWeighed)
        {
            text.Append(" The member placed the screenshot ").Append(Quoted(place)).Append(", so the candidates were weighed by how often GBIF records each ")
                .Append(area).Append('.');

            if (identified)
            {
                text.Append(' ').Append(best.Candidate.ScientificName).Append(best.IsUnrecordedInCountry
                    ? " has no GBIF record anywhere in the country, so it may be planted, or the place or the picture misread."
                    : $" has {Records(best)} there.");
                AppendOthers(text, shown.Skip(1), withRecords: true);
            }
            else
            {
                text.Append(" Likeliest there: ")
                    .Append(string.Join("; ", shown.Select(candidate =>
                        $"{Describe(candidate.Candidate)}, {Percent(candidate.Candidate.Score)} from the picture, {Records(candidate)}")))
                    .Append('.');
            }

            // The picture's favourite, when the records pushed it down: the model should say why it is not the answer.
            var favourite = weighing.Candidates.First(candidate => ReferenceEquals(candidate.Candidate, identification.Best));
            if (!shown.Contains(favourite) && favourite.IsUnrecordedInCountry && place.CountryCode is { } code)
            {
                text.Append(" Pl@ntNet's best match from the picture alone, ").Append(Describe(favourite.Candidate))
                    .Append(" (").Append(Percent(favourite.Candidate.Score)).Append("), has no GBIF record in ")
                    .Append(IsoCountries.NameOf(code)).Append(", so it is unlikely there.");
            }

            if (!identified)
            {
                text.Append(" Say that the plant is uncertain, and give these as the likeliest candidates where it was taken, not as an identification.");
            }
        }
        else
        {
            AppendOthers(text, shown.Skip(1), withRecords: false);

            if (place is not null)
            {
                text.Append(" The member placed the screenshot ").Append(Quoted(place))
                    .Append(", but GBIF's records there could not be read, so the candidates are Pl@ntNet's from the picture alone.");
            }
        }

        text.Append(" The identification is by image recognition and can be wrong; name the plant with its scientific name and the confidence.");

        return new KnowledgeHit(
            // The same verdict for the same place is the same excerpt, so a follow-up on the same screenshot offers it once.
            DeterministicGuid.FromName("plantnet", $"{best.Candidate.ScientificName}|{best.Candidate.Score.ToString("F3", CultureInfo.InvariantCulture)}|{place?.Name}"),
            Score: 0f,
            KnowledgeChunkKind.Text,
            text.ToString(),
            SourceUrl,
            ImageUrl: null,
            SourceTitle,
            Country: null,
            SectionPath: Heading,
            Author: null,
            Priority: 0);
    }

    /// <summary>The excerpt for a screenshot the service saw no plant in.</summary>
    public static KnowledgeHit NotRecognised() =>
        new(
            DeterministicGuid.FromName("plantnet", "not-recognised"),
            Score: 0f,
            KnowledgeChunkKind.Text,
            NotRecognisedText,
            SourceUrl,
            ImageUrl: null,
            SourceTitle,
            Country: null,
            SectionPath: Heading,
            Author: null,
            Priority: 0);

    private static void AppendOthers(StringBuilder text, IEnumerable<WeighedCandidate> others, bool withRecords)
    {
        var listed = others.Select(candidate => withRecords
                ? $"{Describe(candidate.Candidate)}, {Percent(candidate.Candidate.Score)} from the picture, {Records(candidate)}"
                : $"{Describe(candidate.Candidate)} ({Percent(candidate.Candidate.Score)})")
            .ToList();

        if (listed.Count > 0)
        {
            text.Append(" Other candidates: ").Append(string.Join("; ", listed)).Append('.');
        }
    }

    /// <summary>"7,269 records", "1 record", "no records in the whole country", "records not counted".</summary>
    private static string Records(WeighedCandidate candidate) => candidate switch
    {
        { IsUnrecordedInCountry: true } => "no records in the whole country",
        { LocalRecords: 0 } => "no records there, though some elsewhere in the country",
        { LocalRecords: 1 } => "1 record",
        { LocalRecords: { } count } => $"{count.ToString("N0", CultureInfo.InvariantCulture)} records",
        _ => "records not counted"
    };

    /// <summary>"south of Boston (Boston, Massachusetts, United States)", or the place alone when that is what they wrote.</summary>
    private static string Quoted(ScreenshotPlace place) =>
        string.Equals(place.Said, place.Name, StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(place.Said)
            ? $"in {place.Name}"
            : $"\"{place.Said}\" ({place.Name})";

    /// <summary>"Pinus kesiya (Khasi pine, Benguet pine; family Pinaceae)".</summary>
    private static string Describe(PlantCandidate candidate)
    {
        var details = new List<string>();

        var common = candidate.CommonNames.Where(name => !string.IsNullOrWhiteSpace(name)).Take(3).ToList();
        if (common.Count > 0)
        {
            details.Add(string.Join(", ", common));
        }

        if (!string.IsNullOrWhiteSpace(candidate.Family))
        {
            details.Add($"family {candidate.Family}");
        }

        return details.Count == 0
            ? candidate.ScientificName
            : $"{candidate.ScientificName} ({string.Join("; ", details)})";
    }

    /// <summary>"Pinus (Pine, Pines; family Pinaceae)".</summary>
    private static string DescribeGenus(PlantGenusCandidate genus)
    {
        var details = new List<string>();

        var common = genus.CommonNames.Where(name => !string.IsNullOrWhiteSpace(name)).Take(2).ToList();
        if (common.Count > 0)
        {
            details.Add(string.Join(", ", common));
        }

        if (!string.IsNullOrWhiteSpace(genus.Family))
        {
            details.Add($"family {genus.Family}");
        }

        return details.Count == 0 ? genus.Name : $"{genus.Name} ({string.Join("; ", details)})";
    }

    /// <summary>"52 %", "1 %"; a score that would round to nothing is "under 1 %", never "0 %".</summary>
    private static string Percent(double score) =>
        score is > 0 and < 0.005 ? "under 1 %" : $"{Math.Round(score * 100).ToString(CultureInfo.InvariantCulture)} %";
}
