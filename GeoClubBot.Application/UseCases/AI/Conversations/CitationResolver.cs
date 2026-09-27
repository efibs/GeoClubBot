using System.Globalization;
using System.Text.RegularExpressions;

namespace UseCases.UseCases.AI.Conversations;

/// <param name="Number">The number the reader sees, counted in order of first mention.</param>
public sealed record ResolvedCitation(int Number, OfferedExcerpt Excerpt)
{
    /// <summary>A picture is attached as well as listed, so its number anchors an embed too.</summary>
    public bool IsImage => Excerpt.ImageUrl is not null;
}

/// <param name="Text">The answer with every citation rewritten as <c>[n]</c> in the reader's numbering.</param>
/// <param name="Sources">Every cited excerpt, pictures included, in number order — so the list under the answer runs 1, 2, 3.</param>
/// <param name="Images">The cited pictures to attach, in number order, capped.</param>
public sealed record CitationResolution(
    string Text,
    IReadOnlyList<ResolvedCitation> Sources,
    IReadOnlyList<ResolvedCitation> Images);

/// <summary>
/// Turns the citations a model writes into the ones a reader sees.
///
/// Excerpts are offered to the model numbered by retrieval rank, and free models cite them in every
/// style going — <c>[2]</c>, <c>[image 2]</c>, <c>**[image 2]**</c>, <c>【image 2】</c>, <c>[1, 3, 5]</c>,
/// <c>[7-8]</c>, <c>【4:0†source】</c>. Beta testers were shown the results of handling only the first
/// two: image markers deleted outright left <c>see the plate in ****</c> and <c>(, )</c> behind, a
/// full-width marker came through as a literal "image 1" with no picture, and the numbers that did
/// survive ran <c>[6] [1] [4]</c>, "hard to follow… not ordered by number".
///
/// So every citation is resolved to its excerpt whatever it looks like, then renumbered in order of
/// first mention and written back as plain <c>[n]</c>. A picture is cited like any other excerpt and
/// attached as well as listed, so one number means the same thing in the prose, in the source list
/// and on the embed. Numbers that point at nothing are removed along with the space before them.
///
/// Pure, so each of those styles is pinned by a test rather than rediscovered in a user's reply.
/// </summary>
public static partial class CitationResolver
{
    /// <summary>A range wider than this is a typo or a year span rather than a run of citations; only its ends count.</summary>
    private const int MaxRangeSpan = 10;

    // One citation number, 1–99, in ASCII digits. \d would also accept Arabic-Indic and full-width
    // digits, and anything longer is a year or a count — "[2019]" is content, not a citation.
    private const string CitationNumber = "[1-9][0-9]?(?![0-9])";

    // The kind a model writes in front of the number — "image 3", "img3" — ignored when resolving.
    private const string KindPrefix = "(?:(?:image|img|picture|pic)s?[ \\t]*)?";

    private const string Item =
        KindPrefix + "\\#?[ \\t]*" + CitationNumber
        + "(?:[ \\t]*[-–—][ \\t]*" + KindPrefix + CitationNumber + ")?";

    private const string ItemSeparator = "(?:[ \\t]*(?:,|;|/|&|\\band\\b)[ \\t]*|[ \\t]+)";

    // One bracketed citation: [1], [image 2], [1, 3-4], [^5], 【2】, 【4:0†source】. Never a markdown
    // link: "[1](https://…)" is left exactly as written.
    private const string Group =
        "[\\[【［〔][ \\t]*\\^?[ \\t]*"
        + "(?<items>" + Item + "(?:" + ItemSeparator + Item + ")*)"
        + "[ \\t]*(?:(?::[0-9]+)?†[^\\]】］〕\\n]*)?"
        + "[\\]】］〕](?!\\()";

    // Adjacent citations read as one — "[1][2]", "[1], [2]" — together with the parentheses or emphasis
    // wrapped around nothing but them, which would otherwise be left standing as "()" or "****".
    private const string Run =
        "(?<open>\\([ \\t]*)?"
        + "(?<wrap>\\*{1,3}|_{1,2})?"
        + Group + "(?:[ \\t]*[,;]?[ \\t]*" + Group + ")*"
        + "(?(wrap)\\k<wrap>)"
        + "(?(open)[ \\t]*\\))";

    public static CitationResolution Resolve(string answer, IReadOnlyList<OfferedExcerpt> offered, int maxImages)
    {
        ArgumentNullException.ThrowIfNull(answer);
        ArgumentNullException.ThrowIfNull(offered);

        var byMarker = new Dictionary<int, OfferedExcerpt>();
        foreach (var excerpt in offered)
        {
            byMarker.TryAdd(excerpt.Marker, excerpt);
        }

        var numbering = new Dictionary<string, int>(StringComparer.Ordinal);
        var cited = new List<ResolvedCitation>();

        var text = CitationOrCode().Replace(answer, match =>
        {
            if (match.Groups["fence"].Success)
            {
                return match.Value;
            }

            if (match.Groups["code"].Success)
            {
                // A span holding nothing but a citation is a citation someone formatted as code. Left as
                // it is, it would keep the model's number, which now points at a different entry.
                var inner = CitationOnly().Match(match.Value[1..^1]);
                return inner.Success ? Rewrite(inner, lead: string.Empty, trail: string.Empty) : match.Value;
            }

            return Rewrite(match, match.Groups["lead"].Value, match.Groups["trail"].Value);
        });

        var images = cited
            .Where(citation => citation.IsImage)
            .Take(Math.Max(0, maxImages))
            .ToList();

        return new CitationResolution(Tidy(text), cited, images);

        // A run that cites nothing offered is removed together with the whitespace around it that the
        // pattern captured: the space before it mid-line, so no "in ." or "( , )" is left where it
        // stood, or the space after it at the start of a line, so the line does not start with one.
        string Rewrite(Match run, string lead, string trail)
        {
            var numbers = new SortedSet<int>();

            foreach (Capture items in run.Groups["items"].Captures)
            {
                foreach (var marker in ReadMarkers(items.Value))
                {
                    if (!byMarker.TryGetValue(marker, out var excerpt))
                    {
                        continue;
                    }

                    var key = KeyOf(excerpt);
                    if (!numbering.TryGetValue(key, out var number))
                    {
                        number = numbering.Count + 1;
                        numbering[key] = number;
                        cited.Add(new ResolvedCitation(number, excerpt));
                    }

                    numbers.Add(number);
                }
            }

            if (numbers.Count == 0)
            {
                return string.Empty;
            }

            return lead
                   + string.Concat(numbers.Select(number => $"[{number.ToString(CultureInfo.InvariantCulture)}]"))
                   + trail;
        }
    }

    /// <summary>
    /// Removes every citation. Used on replayed answers: their numbers pointed at excerpts of an
    /// earlier turn, and left in they would read as citations of the new turn's excerpts, which are
    /// numbered from one all over again.
    /// </summary>
    public static string StripMarkers(string text) => Resolve(text, [], maxImages: 0).Text;

    private static IEnumerable<int> ReadMarkers(string items)
    {
        foreach (Match item in ItemMarkers().Matches(items))
        {
            var low = int.Parse(item.Groups["low"].ValueSpan, CultureInfo.InvariantCulture);
            if (!item.Groups["high"].Success)
            {
                yield return low;
                continue;
            }

            var high = int.Parse(item.Groups["high"].ValueSpan, CultureInfo.InvariantCulture);
            if (high > low && high - low <= MaxRangeSpan)
            {
                for (var marker = low; marker <= high; marker++)
                {
                    yield return marker;
                }
            }
            else
            {
                yield return low;
                yield return high;
            }
        }
    }

    /// <summary>
    /// Text is deduplicated by page and pictures by picture: two chunks of one Google Doc are one link
    /// to the reader, while two pictures from one album are two different attachments.
    /// </summary>
    private static string KeyOf(OfferedExcerpt excerpt) =>
        excerpt.ImageUrl is { } imageUrl ? $"image|{imageUrl}" : $"text|{excerpt.SourceUrl}";

    /// <summary>
    /// Drops the lines left holding nothing but citations — a model's own "[2] [5] [8]" or "Sources:"
    /// line says nothing the list under the answer does not — and the blank lines that leaves behind.
    /// </summary>
    private static string Tidy(string text)
    {
        var tidied = MarkerOnlyLine().Replace(text, string.Empty);
        tidied = BlankLineRun().Replace(tidied, "\n\n");
        return tidied.Trim();
    }

    // Code is matched first so the citations inside it are skipped. A run at the start of a line takes
    // the whitespace after it; anywhere else it takes only the whitespace before it — taking both would
    // swallow the space the next run needs to take with it when that one is removed too.
    [GeneratedRegex(
        "(?<fence>```[\\s\\S]*?(?:```|\\z))"
        + "|(?<code>`[^`\\n]+`)"
        + "|(?<=^|\\n)(?<lead>[ \\t]*)" + Run + "(?<trail>[ \\t]*)"
        + "|(?<lead>[ \\t]*)" + Run,
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CitationOrCode();

    [GeneratedRegex("^[ \\t]*" + Run + "[ \\t]*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CitationOnly();

    [GeneratedRegex(
        "(?<low>" + CitationNumber + ")(?:[ \\t]*[-–—][ \\t]*" + KindPrefix + "(?<high>" + CitationNumber + "))?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ItemMarkers();

    [GeneratedRegex(
        @"^[ \t]*(?:(?:\*\*)?(?:sources?|references?|citations?)[ \t]*:?(?:\*\*)?[ \t]*:?[ \t]*)?(?:\[[0-9]+\][ \t,;.]*)+$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Multiline)]
    private static partial Regex MarkerOnlyLine();

    [GeneratedRegex(@"\n(?:[ \t]*\n){2,}", RegexOptions.CultureInvariant)]
    private static partial Regex BlankLineRun();
}
