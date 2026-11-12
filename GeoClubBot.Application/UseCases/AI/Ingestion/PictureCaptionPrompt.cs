using System.Text.RegularExpressions;
using UseCases.OutputPorts.AI;
using UseCases.UseCases.AI.Conversations;

namespace UseCases.UseCases.AI.Ingestion;

/// <summary>
/// What a vision model is asked when a guide picture has no words of its own, and what is made of its
/// reply. Pure, so the prompt and the cleaning are pinned by tests.
/// </summary>
public static partial class PictureCaptionPrompt
{
    /// <summary>Low, so the same picture is described much the same way whichever run describes it.</summary>
    public const double Temperature = 0.2;

    /// <summary>Two or three sentences; a model given more room writes a page.</summary>
    public const int MaxTokens = 220;

    /// <summary>
    /// The task. It asks for what a player could recognise a place by, because that is what a question
    /// will be about; a generic "a road with trees" finds nothing. It forbids guessing the country, since
    /// a wrong one would be searched and shown as if the guide said it.
    /// </summary>
    public const string SystemPrompt =
        "You describe pictures from GeoGuessr guides for a search index. Describe only what is visible, "
        + "and of that what helps recognise a place: road markings and surface, bollards, utility poles, "
        + "signs and their shape and colour, licence plates, script and language, vegetation, soil, "
        + "landscape, architecture, vehicles, and camera artefacts. For a map, say which area is "
        + "highlighted or coloured and what the legend says. Write two or three plain sentences and at "
        + "most 60 words. Do not guess the country or region unless its name is written in the picture or "
        + "given to you. Do not mention GeoGuessr, the guide, or that you are describing an image; start "
        + "with the content itself, with no introduction and no list.";

    /// <summary>
    /// The messages for one picture: the task, then where the picture stands in its guide and the
    /// picture itself. The heading is given because it is often the only clue to what the picture is
    /// about — "Bollards" over a picture of a roadside says what to look at.
    /// </summary>
    /// <param name="image">The picture as the model receives it: inline when the relay holds a copy, by URL otherwise.</param>
    public static IReadOnlyList<AiChatMessage> BuildMessages(KnowledgeHit hit, string image)
    {
        ArgumentNullException.ThrowIfNull(hit);

        var where = new List<string>();
        if (!string.IsNullOrWhiteSpace(hit.Title))
        {
            where.Add($"guide \"{hit.Title.Trim()}\"");
        }

        // The path's first segment is the guide's own title, said once already.
        var section = SectionLabel.Clean(hit.SectionPath);
        var title = hit.Title?.Trim() ?? string.Empty;
        if (title.Length > 0 && section.StartsWith(title, StringComparison.OrdinalIgnoreCase))
        {
            section = section[title.Length..].TrimStart(' ', '>', '—').Trim();
        }

        if (!string.IsNullOrWhiteSpace(section))
        {
            where.Add($"section \"{section}\"");
        }

        if (!string.IsNullOrWhiteSpace(hit.Country))
        {
            where.Add($"country {hit.Country.Trim()}");
        }

        var text = where.Count == 0
            ? "Describe this picture."
            : $"Picture from the {string.Join(", ", where)}. Describe it.";

        return
        [
            AiChatMessage.System(SystemPrompt),
            AiChatMessage.User(text, [image])
        ];
    }

    /// <summary>
    /// The description to store, or null when the reply is no description: empty, a safety verdict,
    /// token soup, or the model saying it cannot see the picture. Markdown is flattened and the text
    /// cut to <paramref name="maxLength"/> at a sentence's end where one falls late enough.
    /// </summary>
    public static string? Clean(string? reply, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(reply)
            || GuardrailVerdictDetector.IsVerdict(reply)
            || GarbledTextDetector.IsGarbled(reply))
        {
            return null;
        }

        var flat = Spaces().Replace(Emphasis().Replace(reply, string.Empty), " ").Trim().Trim('"', '“', '”');
        flat = Preamble().Replace(flat, string.Empty).Trim();

        if (flat.Length == 0 || Refusal().IsMatch(flat))
        {
            return null;
        }

        if (char.IsLower(flat[0]))
        {
            flat = string.Concat(char.ToUpperInvariant(flat[0]).ToString(), flat[1..]);
        }

        if (flat.Length <= maxLength)
        {
            return flat;
        }

        var sentenceEnd = flat.LastIndexOfAny(['.', '!', '?'], maxLength - 1);
        if (sentenceEnd >= maxLength / 2)
        {
            return flat[..(sentenceEnd + 1)];
        }

        var wordEnd = flat.LastIndexOf(' ', maxLength - 1);
        return $"{flat[..(wordEnd > maxLength / 2 ? wordEnd : maxLength - 1)].TrimEnd(',', ';', ':', ' ')}…";
    }

    /// <summary>"**", "__", "*", "_" and "#" headings: layout, not words.</summary>
    [GeneratedRegex(@"\*\*|__|(?<!\w)[*_](?=\w)|(?<=\w)[*_](?!\w)|^\s*#+\s*", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex Emphasis();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Spaces();

    /// <summary>"This image shows", "The picture depicts", "In this photo," — the introduction the prompt forbids, written anyway.</summary>
    [GeneratedRegex(@"^(?:(?:this|the)\s+(?:image|picture|photo|photograph|screenshot)\s+(?:shows|depicts|features|displays|contains|captures)\s*(?:of)?|in\s+(?:this|the)\s+(?:image|picture|photo|photograph|screenshot),?)\s*",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Preamble();

    /// <summary>The model saying it will not or cannot, which is no description of anything.</summary>
    [GeneratedRegex(@"^(?:i['’]m\s+sorry|sorry|unfortunately|i\s+(?:can(?:'|’)?t|cannot|am\s+unable|am\s+not\s+able|do\s+not\s+have|don['’]t\s+have)\b|as\s+an\s+ai\b|no\s+image\b|the\s+image\s+(?:is\s+)?(?:not\s+available|could\s+not|cannot|can['’]t)\b)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Refusal();
}
