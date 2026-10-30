using System.Globalization;
using System.Text.RegularExpressions;

namespace UseCases.UseCases.AI.Conversations;

/// <param name="Number">The number the reader sees, counted in order of first mention.</param>
public sealed record ResolvedCitation(int Number, OfferedExcerpt Excerpt)
{
    /// <summary>A picture is attached as well as listed, so its number anchors an embed too.</summary>
    public bool IsImage => Excerpt.ImageUrl is not null;
}

/// <summary>One picture attached to an answer.</summary>
/// <param name="Number">The number of the excerpt it comes with, which its embed shows like the prose.</param>
/// <param name="Position">Which of that excerpt's attached pictures it is, from one.</param>
/// <param name="Count">How many of that excerpt's pictures are attached.</param>
public sealed record ResolvedPicture(int Number, OfferedExcerpt Excerpt, string ImageUrl, int Position, int Count);

/// <param name="Text">The answer with every citation rewritten as <c>[n]</c> in the reader's numbering.</param>
/// <param name="Sources">Every cited excerpt, pictures included, in number order — so the list under the answer runs 1, 2, 3.</param>
/// <param name="Images">The pictures to attach, capped, in number order and each excerpt's own order.</param>
/// <param name="CitedExcerpts">
/// Every excerpt the answer cited, by the number it is listed under and then in order of first
/// mention. Usually one per number; text excerpts of one section of a page share a number, and all of
/// them are here. What a follow-up is offered again.
/// </param>
public sealed record CitationResolution(
    string Text,
    IReadOnlyList<ResolvedCitation> Sources,
    IReadOnlyList<ResolvedPicture> Images,
    IReadOnlyList<OfferedExcerpt> CitedExcerpts);

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
/// An excerpt can come with several pictures (one slide caption beside each of its slide's pictures).
/// All of them may be attached, but the cap counts pictures, not excerpts, and every cited excerpt's
/// first picture comes before any excerpt's second: an answer citing two pictures should show both,
/// not three of the first.
///
/// Two more things reached readers in the answer replay of 2026-10-29 and are tidied here. The
/// prompt's own notes on an excerpt were copied into answers — "[1] (picture, usually with a
/// distribution map) shows the tile style" (#3) — and are removed wherever they stand outside code.
/// And a range written as two citations, "([1]–[6])" (#13), was read as two separate ones: [1], not
/// cited before, became [5] and [6] became [4], so the reader saw "([5]–[4])". Two citations joined by
/// a dash are now the range they mean, bounded like a range in one bracket.
///
/// Pure, so each of those styles is pinned by a test rather than rediscovered in a user's reply.
/// </summary>
public static partial class CitationResolver
{
    /// <summary>A range wider than this is a typo or a year span rather than a run of citations; only its ends count.</summary>
    private const int MaxRangeSpan = 10;

    /// <summary>
    /// The highest number read as a citation; <see cref="CitationNumber"/> spells it out. Nothing may be
    /// offered under a higher one (<see cref="Tools.ExcerptLedger.MaxMarker"/>): it could be cited, but
    /// never resolved to a link or a picture.
    /// </summary>
    public const int MaxCitationNumber = 99;

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

    // One citation in emphasis of its own, "**[1]**", or none. The emphasis is closed as it was opened,
    // then forgotten (the balancing "(?<-mark>)"), so the next citation of the run may have none.
    private const string MarkedGroup =
        "(?<mark>\\*{1,3}|_{1,2})?" + Group + "(?(mark)\\k<mark>(?<-mark>))";

    // Adjacent citations read as one — "[1][2]", "[1], [2]", "[1]–[6]", "**[1]**–**[6]**" — together
    // with the parentheses or emphasis wrapped around nothing but them, which would otherwise be left
    // standing as "()" or "****". A dash between two of them makes them a range (<see cref="ReadRun"/>):
    // read as two runs, "**[1]**–**[6]**" came out as "[2]–[1]" after "[6]" was cited first. Emphasis
    // around each citation is tried before emphasis around the whole run, which would end the run at
    // the first citation's closing marks.
    private const string Run =
        "(?<open>\\([ \\t]*)?"
        + "(?<wrap>\\*{1,3}|_{1,2})??"
        + MarkedGroup + "(?:[ \\t]*(?:(?<dash>[-–—])|[,;])?[ \\t]*" + MarkedGroup + ")*"
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
        var citedExcerpts = new List<(int Number, OfferedExcerpt Excerpt)>();
        var citedMarkers = new HashSet<int>();

        var text = CitationOrCode().Replace(RemoveAnnotations(answer), match =>
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

        return new CitationResolution(
            Tidy(text),
            cited,
            ChoosePictures(cited, maxImages),
            [.. citedExcerpts.OrderBy(entry => entry.Number).Select(entry => entry.Excerpt)]);

        // A run that cites nothing offered is removed together with the whitespace around it that the
        // pattern captured: the space before it mid-line, so no "in ." or "( , )" is left where it
        // stood, or the space after it at the start of a line, so the line does not start with one.
        string Rewrite(Match run, string lead, string trail)
        {
            var numbers = new SortedSet<int>();

            foreach (var marker in ReadRun(run))
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

                if (citedMarkers.Add(marker))
                {
                    citedExcerpts.Add((number, excerpt));
                }

                numbers.Add(number);
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
    /// The pictures to attach: a round of each cited picture excerpt's first picture, in number order,
    /// then a round of second pictures, and so on until <paramref name="maxImages"/> are chosen. A
    /// picture already chosen for another excerpt is not attached twice. Returned in number order, each
    /// excerpt's pictures in their own order.
    /// </summary>
    private static List<ResolvedPicture> ChoosePictures(IReadOnlyList<ResolvedCitation> cited, int maxImages)
    {
        var pictured = cited.Where(citation => citation.IsImage).ToList();
        var chosen = new List<(ResolvedCitation Citation, int Index, string Url)>();
        var taken = new HashSet<string>(StringComparer.Ordinal);
        var budget = Math.Max(0, maxImages);

        for (var round = 0; chosen.Count < budget; round++)
        {
            var anyLeft = false;
            foreach (var citation in pictured)
            {
                var urls = citation.Excerpt.ImageUrls;
                if (round >= urls.Count)
                {
                    continue;
                }

                anyLeft = true;
                if (chosen.Count < budget && taken.Add(urls[round]))
                {
                    chosen.Add((citation, round, urls[round]));
                }
            }

            if (!anyLeft)
            {
                break;
            }
        }

        return
        [
            .. chosen
                .GroupBy(entry => entry.Citation.Number)
                .OrderBy(group => group.Key)
                .SelectMany(group =>
                {
                    var pictures = group.OrderBy(entry => entry.Index).ToList();
                    return pictures.Select((entry, position) => new ResolvedPicture(
                        entry.Citation.Number, entry.Citation.Excerpt, entry.Url, position + 1, pictures.Count));
                })
        ];
    }

    /// <summary>
    /// Removes every citation. Used on replayed answers: their numbers pointed at excerpts of an
    /// earlier turn, and left in they would read as citations of the new turn's excerpts, which are
    /// numbered from one all over again.
    /// </summary>
    public static string StripMarkers(string text) => Resolve(text, [], maxImages: 0).Text;

    /// <summary>
    /// Every marker a run cites, in the order written: each bracket's own, and between two brackets
    /// joined by a dash the numbers they span — "[1]–[6]" is [1] to [6], as "[1-6]" is. A span that runs
    /// backwards or wider than <see cref="MaxRangeSpan"/> is no range, and only its ends count.
    /// </summary>
    private static List<int> ReadRun(Match run)
    {
        var groups = run.Groups["items"].Captures;
        var dashes = run.Groups["dash"].Captures;
        var markers = new List<int>();

        for (var index = 0; index < groups.Count; index++)
        {
            var own = ReadMarkers(groups[index].Value).ToList();

            if (index > 0 && markers.Count > 0 && own.Count > 0
                && dashes.Any(dash => dash.Index > groups[index - 1].Index && dash.Index < groups[index].Index))
            {
                var low = markers[^1];
                var high = own[0];
                if (high > low && high - low <= MaxRangeSpan)
                {
                    markers.AddRange(Enumerable.Range(low + 1, high - low - 1));
                }
            }

            markers.AddRange(own);
        }

        return markers;
    }

    /// <summary>
    /// Removes what the prompt says about an excerpt from an answer that copied it: "(picture)",
    /// "(3 pictures)", "(picture, usually with a distribution map)", "(the user saw this as 2)", and the
    /// distribution-map note written bare after a citation. Code is left exactly as written.
    /// </summary>
    private static string RemoveAnnotations(string answer) =>
        AnnotationOrCode().Replace(answer, match => match.Groups["note"].Success ? string.Empty : match.Value);

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
    /// Text is deduplicated by section of a page and pictures by picture. Excerpts with the same key
    /// share the number the reader sees: two chunks of one Google Doc section are one line under the
    /// answer, while two pictures from one album are two different attachments.
    ///
    /// By section, not by page: every text chunk of a Google Doc shares the document's one link, and
    /// keyed by the link alone an answer listing nine US states cited all of them as one "[2] … Counties
    /// (33)", and a Baja California answer cited the guide's whole plant list as "[2] Mexico
    /// Deconstructed > Boojum tree" (2026-10-29). The section is the heading the reader sees
    /// (<see cref="OfferedExcerpt.Label"/>), so two sections that read alike are still one line.
    /// </summary>
    internal static string KeyOf(OfferedExcerpt excerpt) =>
        excerpt.ImageUrl is { } imageUrl ? $"image|{imageUrl}" : $"text|{SectionKeyOf(excerpt)}";

    /// <summary>
    /// The page and the heading an excerpt is listed under: what makes two excerpts one line to a
    /// reader, picture or not. The guides credited for an answer that cited none are told apart by it.
    /// </summary>
    internal static string SectionKeyOf(OfferedExcerpt excerpt) => $"{excerpt.SourceUrl}|{excerpt.Label}";

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

    // The prompt's notes on an excerpt (AiPromptBuilder.FormatExcerpt), in brackets and perhaps in
    // emphasis, with the whitespace before them; or the map note written bare after a citation. Code
    // is matched first so nothing inside it is touched.
    [GeneratedRegex(
        "(?<fence>```[\\s\\S]*?(?:```|\\z))"
        + "|(?<code>`[^`\\n]+`)"
        + "|(?<note>[ \\t]*(?<w>\\*{1,3}|_{1,2})?\\([ \\t]*(?:"
        + "(?:pictures?|[0-9]{1,2}[ \\t]+pictures)(?:[ \\t]*,[ \\t]*" + AiPromptBuilder.DistributionMapNote + ")?"
        + "|" + AiPromptBuilder.DistributionMapNote
        + "|the user saw this as [0-9]{1,2}"
        + ")[ \\t]*\\)(?(w)\\k<w>))"
        + "|(?<=[\\]】］〕])(?<note>[ \\t]*[,:–—-]?[ \\t]*" + AiPromptBuilder.DistributionMapNote + "\\b,?)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AnnotationOrCode();

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
