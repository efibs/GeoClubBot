using System.Text.RegularExpressions;

namespace UseCases.UseCases.AI.Conversations;

/// <summary>
/// Turns a chunk's section path into the label a reader and the model see, and says which headings
/// are not headings at all.
///
/// Google Docs lose their heading markup in the text export, and many guides never used heading
/// styles, so the extractor took any short line standing alone for a heading. Under the answers of the
/// 2026-10-29 replay that put link text, credits, captions and arrows where a section's name belongs:
/// "Usa · A Unique USA > Counties (33) ->", "… > You can view the flags in more detail here",
/// "… > Image credit to PlonkIt", "… > @bagaboiebailey", "… > https://goo.gl/maps/…",
/// "… > Research by Yaron🐐", "… > ________________", "… > you rn". The extractor no longer makes such
/// lines headings, but the chunks already indexed keep their paths until their guide is next checked
/// (within <c>AI:Ingestion:ReingestAfterDays</c>; a changed heading changes the guide's chunks, so it is
/// embedded again then), so every label is cleaned when it is shown as well: trailing arrows and
/// credits are cut, columns of a table read as one, and a segment that is not a heading is dropped.
/// The guide's own title, the first segment, is always kept.
///
/// Dropping a real heading costs more than keeping a poor one. Shown, it only shortens a label; but
/// in the extractor the line becomes text, and every chunk under it is filed under the heading before
/// it, so the model is told the bollard paragraphs are about poles. So the rule is held to the shapes
/// the replay showed, and was checked against all 293 section names of its cached prompts: the real
/// ones it once dropped — "Bollards : (they are also present in the Czech Republic)", "Common roof in
/// NE India (similar to the roofs in Bangladesh)", "blue gen 4 with antenna" — are kept, and so are
/// "Credit unions and banks", "Water tap covers" and "Roads made by the army".
///
/// Pure, so each case is pinned by a test.
/// </summary>
public static partial class SectionLabel
{
    /// <summary>The separator the extractors join a section path with.</summary>
    public const string Separator = " > ";

    /// <summary>
    /// Headings are a few words. Longer is a sentence or a caption: "In the Kiso mountain region red
    /// roofs are very common", "A more distinct style that can be found here and in nearby Hokuriku".
    /// Counted outside brackets, where a heading keeps its aside: "Common roof in NE India (similar to
    /// the roofs in Bangladesh)" is five words. In the replay's 293 section names the real headings ran
    /// to eight words, the sentences and captions from nine; and a caption taken for a heading would
    /// also cost the picture under it its caption.
    /// </summary>
    private const int MaxHeadingWords = 8;

    /// <summary>
    /// An all-lower-case line this short is a remark, not a heading: "you rn". Longer, or with a
    /// capital or a digit in it, it is a heading written casually: "blue gen 4 with antenna",
    /// "km markers and road signs", "eSIM and mobile data".
    /// </summary>
    private const int MaxRemarkWords = 3;

    /// <summary>The label for <paramref name="path"/>, or an empty string when nothing in it is a heading.</summary>
    public static string Clean(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var kept = new List<string>();
        foreach (var raw in path.Split(Separator, StringSplitOptions.None))
        {
            var segment = Normalise(raw);

            // The guide's title stands whatever it looks like: it names the guide, not a section of it.
            if (kept.Count == 0)
            {
                if (segment.Length > 0)
                {
                    kept.Add(segment);
                }

                continue;
            }

            if (IsNotAHeading(segment) || string.Equals(segment, kept[^1], StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            kept.Add(segment);
        }

        return string.Join(Separator, kept);
    }

    /// <summary>
    /// Whether a line that stands alone like a heading is something else: link text, a credit, a URL,
    /// filler, a sentence, or a pointer to what follows. Shared with the extractors, so what they no
    /// longer take for a heading and what a label no longer shows are one rule.
    /// </summary>
    public static bool IsNotAHeading(string? text)
    {
        var segment = Normalise(text ?? string.Empty);
        if (IsNeverAHeadingNormalised(segment))
        {
            return true;
        }

        // Counted outside brackets; a heading wholly in brackets, "(gen2)", by what they hold.
        var words = Words(Bracketed().Replace(segment, " "));
        if (words.Count == 0)
        {
            words = Words(segment);
        }

        return words.Count > MaxHeadingWords
               || (words.Count > 1 && words.Count <= MaxRemarkWords && IsAllLowerCase(segment))
               || PointsElsewhere().IsMatch(segment);
    }

    /// <summary>
    /// Whether a line is no heading whatever it is styled as: a URL, a credit, or no words at all. All
    /// that a heading element of a document is judged by — its author chose to make it one, so neither
    /// its length nor its case says otherwise.
    /// </summary>
    public static bool IsNeverAHeading(string? text) => IsNeverAHeadingNormalised(Normalise(text ?? string.Empty));

    private static bool IsNeverAHeadingNormalised(string segment) =>
        segment.Length == 0
        || !Letter().IsMatch(segment)
        || Url().IsMatch(segment)
        || Credit().IsMatch(segment);

    /// <summary>
    /// Every letter lower case and no digit: "you rn", not "eSIM and mobile data" — nor a script without
    /// case, whose letters are none of them lower case.
    /// </summary>
    private static bool IsAllLowerCase(string text) =>
        !text.Any(char.IsDigit) && text.Where(char.IsLetter).All(char.IsLower);

    /// <summary>The words of a line: what holds a letter or a digit, so a lone ":" or "/" is none.</summary>
    private static List<string> Words(string text) =>
        [.. text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(word => word.Any(char.IsLetterOrDigit))];

    /// <summary>Whether a line ends in an arrow, pointing at a link or a picture rather than naming a section.</summary>
    public static bool EndsWithArrow(string? text) => !string.IsNullOrWhiteSpace(text) && TrailingArrow().IsMatch(text.TrimEnd());

    /// <summary>
    /// One segment as a label shows it: invisible marks removed, a table's columns joined with " / ",
    /// a trailing arrow or colon and a credit in brackets cut.
    /// </summary>
    private static string Normalise(string segment)
    {
        var text = Invisible().Replace(segment, string.Empty);
        text = Columns().Replace(text.Trim(), " / ");
        text = Spaces().Replace(text, " ");
        text = BracketedCredit().Replace(text, string.Empty);
        text = TrailingArrow().Replace(text, string.Empty);
        return text.Trim();
    }

    [GeneratedRegex(@"\p{L}", RegexOptions.CultureInvariant)]
    private static partial Regex Letter();

    [GeneratedRegex(@"https?://|www\.|\b[a-z0-9-]+\.(?:com|net|org|gl|me|ly|io|co)/", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Url();

    // "Image credit to PlonkIt", "Credits: …", "Research by Yaron", "Author and all research done by:
    // Kyleb_b", "@bagaboiebailey", "Thanks to …", "Courtesy of …". Each in the shape a credit takes, so
    // "Credit unions and banks" and "Roads made by the army" are headings: "… by" only opens the line
    // or follows a colon.
    [GeneratedRegex(
        @"^@|\bcredits?\s*(?:to\b|:)|\b(?:image|photo|picture|map)\s+credits?\b|\bcourtesy\s+of\b|^thanks\b|^author\b"
        + @"|(?:^|:\s*)(?:(?:all\s+)?research|made|written|compiled|created|done)\s+by\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Credit();

    // "You can view the flags in more detail here", "Click here", "see below" — but "Tap signs" and
    // "Water tap covers" are headings.
    [GeneratedRegex(@"\bhere$|\b(?:click|tap)\s+here\b|\bsee\s+(?:below|above)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PointsElsewhere();

    // An aside in brackets, which a heading's word count leaves out.
    [GeneratedRegex(@"\([^()]*\)|\[[^\[\]]*\]", RegexOptions.CultureInvariant)]
    private static partial Regex Bracketed();

    [GeneratedRegex(@"[ \t]*(?:-+>|=+>|[→⇒➜➔»›:]|\s-+)+$", RegexOptions.CultureInvariant)]
    private static partial Regex TrailingArrow();

    [GeneratedRegex(@"[ \t]*\((?:[^()]*\b(?:credits?|courtesy|thanks)\b[^()]*)\)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BracketedCredit();

    [GeneratedRegex("[﻿​-‍⁠]", RegexOptions.CultureInvariant)]
    private static partial Regex Invisible();

    [GeneratedRegex(@"[ \t ]{3,}", RegexOptions.CultureInvariant)]
    private static partial Regex Columns();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Spaces();
}
