using System.Text.RegularExpressions;

namespace UseCases.UseCases.AI.Conversations;

/// <summary>
/// The line shown under a picture's title in an answer: what the guide says the picture shows, short
/// enough to read at a glance.
///
/// A picture used to carry its number and its guide's name, and nothing of what it was. Five pictures of
/// five clues from one document stood under one title, and the member who asked wrote "it is hard to
/// understand which image is which clue" (2026-11-09). The caption is the start of the excerpt's own
/// text — for a clue written as a card, its name and the first of what is said about it.
///
/// Pure, so the cut is pinned by a test.
/// </summary>
public static partial class PictureCaption
{
    /// <summary>Longest caption. Ten pictures' titles and captions must stay within Discord's 6,000 characters per message.</summary>
    public const int MaxLength = 200;

    /// <summary>A cut is made at a sentence's end only when that leaves at least this much.</summary>
    private const int MinSentenceCut = 60;

    /// <summary>
    /// The caption for a picture whose excerpt reads <paramref name="text"/>, or null when the text says
    /// nothing its heading does not: a picture filed under a heading alone is named by its title.
    /// </summary>
    public static string? Of(string? text, string? sectionPath, string? title)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        // Emphasis marks are the guide's layout, not its words; a bullet's dash likewise.
        var flat = Spaces().Replace(Emphasis().Replace(text, string.Empty), " ").Trim();
        flat = LeadingBullet().Replace(flat, string.Empty);

        if (flat.Length == 0
            || string.Equals(flat, Flatten(sectionPath), StringComparison.OrdinalIgnoreCase)
            || string.Equals(flat, Flatten(title), StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (flat.Length <= MaxLength)
        {
            return flat;
        }

        var sentenceEnd = flat.LastIndexOfAny(['.', '!', '?'], MaxLength - 1);
        if (sentenceEnd >= MinSentenceCut)
        {
            return flat[..(sentenceEnd + 1)];
        }

        var wordEnd = flat.LastIndexOf(' ', MaxLength - 1);
        return $"{flat[..(wordEnd > MinSentenceCut ? wordEnd : MaxLength - 1)].TrimEnd(',', ';', ':', ' ')}…";
    }

    private static string Flatten(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : Spaces().Replace(value, " ").Trim();

    [GeneratedRegex(@"\*+|(?<![\p{L}\p{N}])_+|_+(?![\p{L}\p{N}])", RegexOptions.CultureInvariant)]
    private static partial Regex Emphasis();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Spaces();

    [GeneratedRegex(@"^[-–•]\s*", RegexOptions.CultureInvariant)]
    private static partial Regex LeadingBullet();
}
