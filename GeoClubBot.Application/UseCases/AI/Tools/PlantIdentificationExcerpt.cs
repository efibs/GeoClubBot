using System.Globalization;
using System.Text;
using UseCases.OutputPorts.AI;
using Utilities;

namespace UseCases.UseCases.AI.Tools;

/// <summary>
/// What Pl@ntNet made of the screenshot, as one excerpt among the guides', so the model names the plant
/// from it instead of from a look-alike guide picture. Pure, so the wording is pinned by a test.
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
    /// The candidates with their scores, and a verdict: the best one when it clears
    /// <paramref name="minConfidence"/>, a guess otherwise. Said as a fact the model may repeat, with
    /// the score, so a member can tell a sure identification from a toss-up.
    /// </summary>
    public static KnowledgeHit ToHit(PlantIdentification identification, double minConfidence, int maxCandidates)
    {
        ArgumentNullException.ThrowIfNull(identification);

        var candidates = identification.Candidates.Take(Math.Max(1, maxCandidates)).ToList();
        var best = candidates[0];

        var text = new StringBuilder();
        if (best.Score >= minConfidence)
        {
            text.Append("Pl@ntNet identifies the plant in the screenshot as ")
                .Append(Describe(best))
                .Append(" with ").Append(Percent(best.Score)).Append(" confidence.");
        }
        else
        {
            text.Append("Pl@ntNet is not sure what the plant in the screenshot is; its best guess is ")
                .Append(Describe(best))
                .Append(" at only ").Append(Percent(best.Score)).Append(" confidence, so say it is a guess.");
        }

        if (candidates.Count > 1)
        {
            text.Append(" Other candidates: ")
                .Append(string.Join("; ", candidates.Skip(1).Select(candidate => $"{Describe(candidate)} ({Percent(candidate.Score)})")))
                .Append('.');
        }

        text.Append(" The identification is by image recognition and can be wrong; name the plant with its scientific name and the confidence.");

        return new KnowledgeHit(
            // The same best candidate is the same excerpt, so a follow-up on the same screenshot offers it once.
            DeterministicGuid.FromName("plantnet", $"{best.ScientificName}|{best.Score.ToString("F3", CultureInfo.InvariantCulture)}"),
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

    private static string Percent(double score) =>
        $"{Math.Round(score * 100).ToString(CultureInfo.InvariantCulture)} %";
}
