using System.Globalization;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using UseCases.UseCases.AI.Conversations;

namespace Infrastructure.OutputAdapters.AI.Extractors;

/// <summary>
/// One piece of a Google document in reading order: a paragraph, or a picture with the words that say
/// what it shows.
/// </summary>
/// <param name="SectionPath">The headings the piece sits under, the document's title first.</param>
/// <param name="Anchor">The link fragment that opens the document at the piece's heading; null when the heading has none.</param>
/// <param name="Text">A paragraph's text, or a picture's caption (empty when nothing near it says what it shows).</param>
/// <param name="ImageSource">The picture's <c>src</c> as the document writes it; null for a paragraph.</param>
/// <param name="Group">Pictures captioned by the same words share a group; a paragraph has none.</param>
/// <param name="OwnsText">
/// The caption holds paragraphs that are in no text piece. Should every picture of the group be lost,
/// the caption has to be kept as text, or those paragraphs leave the index with the pictures.
/// </param>
internal sealed record GoogleDocPiece(
    string SectionPath,
    string? Anchor,
    string Text,
    string? ImageSource = null,
    int? Group = null,
    bool OwnsText = false);

/// <summary>
/// Reads a Google document's HTML — the mobile page or the archive export, which are shaped alike — into
/// its paragraphs and pictures.
///
/// Three things here answer the feedback of 2026-11-09.
///
/// <b>Sections are the headings' own nesting.</b> "A Unique USA" has 693 headings, three deep, and the same
/// "State Route Shield" under every state. Filed under the nearest heading alone, as every document was,
/// that chunk said nothing of which state it described. The path now keeps up to
/// <see cref="MaxPathHeadings"/> levels ("7.20 Maryland (MD) > 7.20.1 Signs > State Route Shield"). A
/// heading shallower than the document's first is held at the first one's level: authors who use two
/// heading styles at random ("A Learnable Colombia" has chapters as Heading 2 and three plants as Heading
/// 1) would otherwise file "Cacti" under "Paramos".
///
/// <b>A picture is captioned by the words around it, and takes them with it.</b> These guides are written
/// as cards: a title line, the picture, then what it shows ("Ohio gap ** (Photo : West Liberty, Ohio):",
/// picture, "In Ohio, you can often see gaps in road lines where there is a RPM"). Captioned by the line
/// before it alone, the picture reached the model as a title, and the model made up the rest ("a telltale
/// gap in pavement or road markings"); the explanation sat in a text chunk without the picture. So a
/// picture's caption is its lead-in and the paragraph that follows, and those paragraphs are in no text
/// chunk of their own: whoever finds the words gets the picture. In a table row the row's words caption
/// its pictures, since these documents lay a picture and its note out side by side.
///
/// <b>A bold line above a paragraph is a heading too.</b> Authors who style some headings leave others as
/// a line in bold ("4. Oil Palms:" above "Are most commonly found in Meta and Casanare"), and the
/// paragraphs under such a line say nothing of their subject. It nests under the styled heading it
/// stands in, unless it is numbered like one that is open ("Chapter 2" after "Chapter 1"), whose place
/// it then takes. A bold line straight above a picture is that picture's caption, not a heading.
///
/// <b>A heading's link.</b> A heading with an id can be linked to, so its pieces carry the fragment, and
/// each cited section of a document is a link of its own.
///
/// Pure: the pictures are named by their <c>src</c> and fetched by the caller.
/// </summary>
internal static partial class GoogleDocHtml
{
    /// <summary>A line this short, standing alone, may be a heading in a document that styles none.</summary>
    private const int MaxHeadingLength = 80;

    /// <summary>
    /// Heading elements a document needs before they alone mark its sections. One or two are often a
    /// title styled as a heading above sections in bold, whose names only the shape of a line gives
    /// away; reading such a document by its styles would lose every section name in it.
    /// </summary>
    private const int MinStyledHeadings = 3;

    /// <summary>Levels of heading kept in a section path, innermost last.</summary>
    private const int MaxPathHeadings = 3;

    /// <summary>
    /// The longest paragraph a picture takes out of the text chunks and into its caption. A picture's
    /// chunk is never split, so a longer one stays text and lends the picture only its nearest part.
    /// </summary>
    private const int MaxAbsorbedCharacters = 800;

    /// <summary>The most a paragraph that stays text lends to a picture's caption.</summary>
    private const int MaxLentCharacters = 400;

    /// <summary>
    /// A caption shorter than this is given the sentence it follows: "Ex: Silver car" says too little to
    /// be found by, and a note under a map ("Buenavista is so small it doesn't pop up here") too little
    /// of what the map is.
    /// </summary>
    private const int ShortCaptionCharacters = 160;

    /// <summary>The longest line under a picture taken for its caption when nothing above it is one.</summary>
    private const int MaxCaptionBelowCharacters = 200;

    /// <summary>The level of a heading that is only a line in bold: below every heading element.</summary>
    private const int BoldLineLevel = 7;

    /// <summary>Share of a line's letters in bold from which the line counts as bold.</summary>
    private const double BoldShare = 0.5;

    /// <summary>A line of a list is at most this long; three in a row are a list, not a picture's lead-in.</summary>
    private const int MaxListLineLength = 60;

    /// <summary>How many pieces back that sentence is looked for.</summary>
    private const int ContextLookBack = 8;

    /// <summary>A line no longer than this that does not end a sentence introduces what follows it.</summary>
    private const int MaxLeadInLength = 90;

    private static readonly string[] BlockElements = ["p", "h1", "h2", "h3", "h4", "h5", "h6", "li"];

    private static readonly HashSet<string> BlockElementNames = new(BlockElements, StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> HeadingElements = new(StringComparer.OrdinalIgnoreCase) { "h1", "h2", "h3", "h4", "h5", "h6" };

    /// <summary>The document's pieces in reading order; empty when the HTML holds no document.</summary>
    /// <param name="defaultSection">The section of what stands before the first heading, and the root of every path.</param>
    public static IReadOnlyList<GoogleDocPiece> Read(string html, string defaultSection)
    {
        ArgumentNullException.ThrowIfNull(html);

        var blocks = ReadBlocks(html);
        if (blocks.Count == 0)
        {
            return [];
        }

        MarkHeadings(blocks);
        AssignSections(blocks, defaultSection);

        var content = blocks.Where(block => !block.IsHeading && (block.Text.Length > 0 || block.Images.Count > 0)).ToList();
        CaptionPictures(content);

        var pieces = new List<GoogleDocPiece>();
        foreach (var block in content)
        {
            if (block.Text.Length > 0 && !block.Absorbed)
            {
                pieces.Add(new GoogleDocPiece(block.Section, block.Anchor, block.Text));
            }

            foreach (var image in block.Images)
            {
                // A picture's own description, where its author wrote one, says what the lines around it may not.
                var caption = block.Caption ?? string.Empty;
                if (image.Description.Length > 0 && !caption.Contains(image.Description, StringComparison.OrdinalIgnoreCase))
                {
                    caption = caption.Length == 0 ? image.Description : $"{caption}\n{image.Description}";
                }

                pieces.Add(new GoogleDocPiece(block.Section, block.Anchor, caption, image.Source, block.Group, block.OwnsText));
            }
        }

        return pieces;
    }

    /// <summary>
    /// Whether a line of a document that styles no headings is one, by its shape: short, standing alone,
    /// not punctuated like a sentence, and none of the lines that are shaped like a heading but are not
    /// (<see cref="SectionLabel.IsNotAHeading"/>): credits, URLs, filler, captions, or a line ending in an
    /// arrow. The text export, which loses every heading's markup, is read by this alone.
    /// </summary>
    public static bool LooksLikeHeading(string paragraph) =>
        paragraph.Length <= MaxHeadingLength
        && !paragraph.Contains('\n')
        && !paragraph.EndsWith('.')
        && !paragraph.EndsWith(',')
        && !paragraph.EndsWith(':')
        && !SectionLabel.EndsWithArrow(paragraph)
        && !SectionLabel.IsNotAHeading(paragraph);

    private static List<Block> ReadBlocks(string html)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html);

        var nodes = document.DocumentNode.SelectNodes(
            string.Join(" | ", BlockElements.Select(element => $"//body//{element}")));

        var blocks = new List<Block>();
        foreach (var node in nodes ?? Enumerable.Empty<HtmlNode>())
        {
            // The innermost block holds the words, so a cell's paragraph inside a list item is read once.
            if (node.Descendants().Any(descendant => BlockElementNames.Contains(descendant.Name)))
            {
                continue;
            }

            var text = Normalise(HtmlText.Decode(node.InnerText));
            var images = node.Descendants("img")
                .Select(image => new Picture(
                    HtmlText.Decode(image.GetAttributeValue("src", string.Empty)).Trim(),
                    Describe(image)))
                .Where(image => image.Source.Length > 0)
                .ToList();

            var row = node.Ancestors("tr").FirstOrDefault();
            var isHeadingElement = HeadingElements.Contains(node.Name);
            var isBold = text.Length > 0 && images.Count == 0 && !isHeadingElement && IsBoldLine(node);

            // A heading holding a picture is a heading, and then a picture under it.
            if (isHeadingElement && text.Length > 0 && images.Count > 0)
            {
                blocks.Add(new Block(node, text, [], row));
                blocks.Add(new Block(node, string.Empty, images, row) { IsPictureOfHeading = true });
                continue;
            }

            blocks.Add(new Block(node, text, images, row) { IsBold = isBold });
        }

        return blocks;
    }

    /// <summary>
    /// Whether a line is set in bold: it opens in bold, and at least <see cref="BoldShare"/> of it is.
    /// The page writes each run's weight inline; the archive export keeps it in a style sheet, so its
    /// lines are never bold here and its documents are read by their heading elements alone.
    /// </summary>
    private static bool IsBoldLine(HtmlNode block)
    {
        var total = 0;
        var bold = 0;
        bool? opensBold = null;

        foreach (var run in block.Descendants().Where(node => node.NodeType == HtmlNodeType.Text))
        {
            var length = Normalise(HtmlText.Decode(run.InnerText)).Length;
            if (length == 0)
            {
                continue;
            }

            var isBold = IsBold(run, block);
            opensBold ??= isBold;
            total += length;
            bold += isBold ? length : 0;
        }

        return opensBold == true && total > 0 && bold >= total * BoldShare;
    }

    private static bool IsBold(HtmlNode run, HtmlNode block)
    {
        for (var node = run.ParentNode; node is not null; node = node.ParentNode)
        {
            if (node.Name is "b" or "strong")
            {
                return true;
            }

            var weight = FontWeight().Match(node.GetAttributeValue("style", string.Empty));
            if (weight.Success)
            {
                var value = weight.Groups["weight"].Value;
                return value.StartsWith("bold", StringComparison.OrdinalIgnoreCase)
                       || (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number >= 600);
            }

            if (ReferenceEquals(node, block))
            {
                break;
            }
        }

        return false;
    }

    /// <summary>What a picture's author wrote of it: its alternative text or its title, when either says anything.</summary>
    private static string Describe(HtmlNode image)
    {
        var description = Normalise(HtmlText.Decode(image.GetAttributeValue("alt", string.Empty)));
        if (description.Length == 0)
        {
            description = Normalise(HtmlText.Decode(image.GetAttributeValue("title", string.Empty)));
        }

        return SectionLabel.IsNeverAHeading(description) ? string.Empty : description;
    }

    /// <summary>
    /// A heading element is a heading unless it is a credit, a URL or has no words
    /// (<see cref="SectionLabel.IsNeverAHeading"/>) — never for its length or its case, since its author
    /// chose to make it one, and a heading lost files every chunk under it under the heading before. In a
    /// document that does not mark its sections with heading elements, a line shaped like a heading is
    /// one too, unless it is a list item, holds a picture, or is nothing but a link: "Counties (33) ->"
    /// and "You can view the flags in more detail here" were links, and became the section of every
    /// chunk after them.
    /// </summary>
    private static void MarkHeadings(List<Block> blocks)
    {
        var styled = blocks.Count(block => block.IsHeadingElement && block.Text.Length > 0) >= MinStyledHeadings;

        // What stands on the page: the blocks with words or pictures, which is what a line is "above".
        var standing = blocks.Where(block => block.Text.Length > 0 || block.Images.Count > 0).ToList();

        for (var index = 0; index < standing.Count; index++)
        {
            var block = standing[index];
            if (block.Text.Length == 0 || block.Images.Count > 0 || block.IsPictureOfHeading)
            {
                continue;
            }

            if (block.IsHeadingElement)
            {
                block.IsHeading = !SectionLabel.IsNeverAHeading(block.Text);
                block.Level = block.Node.Name[1] - '0';
            }
            else if (!styled)
            {
                if (!string.Equals(block.Node.Name, "li", StringComparison.OrdinalIgnoreCase)
                    && LooksLikeHeading(block.Text)
                    && !IsOnlyALink(block.Node, block.Text))
                {
                    block.IsHeading = true;
                    block.Level = 1;
                }
            }
            else if (IsBoldHeading(block, index > 0 ? standing[index - 1] : null, index + 1 < standing.Count ? standing[index + 1] : null))
            {
                block.IsHeading = true;
                block.Level = BoldLineLevel;
            }
        }
    }

    /// <summary>
    /// Whether a bold line of a document that styles its headings is one more of them: short, no
    /// sentence, no credit or link, and either numbered ("Chapter 2: Road markers", "4. Oil Palms:") or
    /// standing above a paragraph. Unnumbered, it is a picture's caption above a picture, a row of a
    /// list or of a table of contents beside another bold line, and an introduction when it ends in a
    /// colon ("EX:", "Chapters:").
    /// </summary>
    private static bool IsBoldHeading(Block block, Block? previous, Block? next)
    {
        if (!block.IsBold || block.Row is not null || next is null)
        {
            return false;
        }

        var text = block.Text;
        if (text.Length > MaxHeadingLength || EndsSentence(text) || text.EndsWith(',')
            || SectionLabel.IsNeverAHeading(text) || IsOnlyALink(block.Node, text))
        {
            return false;
        }

        if (OutlineKey(text) is not null)
        {
            return true;
        }

        return !text.EndsWith(':') && next.Images.Count == 0 && !next.IsBold && !next.IsHeadingElement
               && previous is not { IsBold: true };
    }

    /// <summary>
    /// What a heading is numbered like: "chapter" for "Chapter 2: Road markers", the depth of its number
    /// for "7.21.3 Inspection Stickers" and "4. Oil Palms:"; null when it is not numbered.
    /// </summary>
    private static string? OutlineKey(string text)
    {
        var numbered = OutlineNumber().Match(text);
        if (numbered.Success)
        {
            return $"#{numbered.Groups["number"].Value.Split('.', StringSplitOptions.RemoveEmptyEntries).Length.ToString(CultureInfo.InvariantCulture)}";
        }

        var worded = OutlineWord().Match(text);
        return worded.Success ? worded.Groups["word"].Value.ToLowerInvariant() : null;
    }

    private static void AssignSections(List<Block> blocks, string defaultSection)
    {
        int? rootLevel = null;
        var open = new List<(int Level, string Text, string? Id)>();

        foreach (var block in blocks)
        {
            if (block.IsHeading)
            {
                int level;
                if (block.Level == BoldLineLevel)
                {
                    // Numbered like a heading that is open, a bold line takes that heading's place.
                    var key = OutlineKey(block.Text);
                    var sibling = key is null ? default : open.LastOrDefault(heading => OutlineKey(heading.Text) == key);
                    level = sibling.Text is null ? BoldLineLevel : sibling.Level;
                }
                else
                {
                    rootLevel ??= block.Level;

                    // Never above the document's first heading: see the class remarks.
                    level = Math.Max(block.Level, rootLevel.Value);
                }

                open.RemoveAll(heading => heading.Level >= level);

                var id = block.Node.GetAttributeValue("id", string.Empty);
                open.Add((level, block.Text, id.Length > 0 ? id : null));
                continue;
            }

            block.Section = open.Count == 0
                ? defaultSection
                : $"{defaultSection}{SectionLabel.Separator}{string.Join(SectionLabel.Separator, open.TakeLast(MaxPathHeadings).Select(heading => heading.Text))}";

            // The nearest heading that can be linked to; an outer one still opens the right part of the document.
            block.Anchor = open.LastOrDefault(heading => heading.Id is not null).Id is { } anchor ? $"heading={anchor}" : null;
        }
    }

    /// <summary>
    /// Gives every picture its caption. Pictures standing together are one run and share it.
    ///
    /// In a table row, the row's words. Otherwise the line above the run, and the paragraph below it
    /// when that says what the picture shows: when the line above only introduces the picture and the
    /// paragraph below speaks of the same thing ("Ohio gap **:" above, "In Ohio, you can often see gaps
    /// in road lines" below) or points back at it ("These little bumps…"); or when nothing stands above
    /// it. A paragraph below that is about something else is the next subject, and is left alone: "White
    /// car ex:" is not explained by "In A Learnable Colombia you will mostly find Gen 4 coverage". A
    /// paragraph straight above the next picture introduces that one, and a paragraph one run took is not
    /// also the next run's line above. Credits under a run go with it. A caption that says little is
    /// given the sentence it follows, which stays a paragraph of its own.
    /// </summary>
    private static void CaptionPictures(List<Block> content)
    {
        var rowsWithWords = content
            .Where(block => block.Row is not null && block.Text.Length > 0)
            .Select(block => block.Row!)
            .ToHashSet();

        bool InRowWithWords(Block candidate) => candidate.Row is not null && rowsWithWords.Contains(candidate.Row);

        var group = 0;

        for (var index = 0; index < content.Count; index++)
        {
            var block = content[index];
            if (block.Images.Count == 0 || block.Group is not null)
            {
                continue;
            }

            group++;

            if (InRowWithWords(block))
            {
                CaptionRow(content, block.Row!, group);
                continue;
            }

            // The run: this block and the bare pictures straight after it.
            var end = index;
            while (end + 1 < content.Count && IsBarePicture(content[end + 1]) && content[end + 1].Group is null
                   && SameSection(content[end + 1], block) && !InRowWithWords(content[end + 1]))
            {
                end++;
            }

            Block? before = null;
            if (block.Text.Length > 0)
            {
                before = block;
            }
            else if (index > 0 && IsPlainText(content[index - 1]) && SameSection(content[index - 1], block)
                     && !content[index - 1].TakenByPictureBefore && !InRowWithWords(content[index - 1])
                     // A link or a credit above a picture says nothing of it: "https://maps.app.goo.gl/…".
                     && !SectionLabel.IsNeverAHeading(content[index - 1].Text)
                     && !IsListTail(content, index - 1))
            {
                before = content[index - 1];
            }

            // Nothing but links and credits between the heading and the run: what follows is about it.
            var above = index - 1;
            while (above >= 0 && IsPlainText(content[above]) && SameSection(content[above], block)
                   && SectionLabel.IsNeverAHeading(content[above].Text))
            {
                above--;
            }

            var opensSection = before is null && (above < 0 || !SameSection(content[above], block));

            var parts = new List<string>();
            var owns = false;

            if (before is not null)
            {
                owns |= Take(before, parts, fromEnd: true);
            }

            // Credits straight under the pictures are theirs, and are passed over to find what is said of them.
            var next = end + 1;
            var credits = new List<Block>();
            while (next < content.Count && IsPlainText(content[next]) && SameSection(content[next], block)
                   && !InRowWithWords(content[next]) && SectionLabel.IsNeverAHeading(content[next].Text))
            {
                if (IsCredit(content[next].Text))
                {
                    credits.Add(content[next]);
                }

                next++;
            }

            var explained = false;
            if (FindBelow(content, next, block, before, opensSection) is { } below && !InRowWithWords(below))
            {
                below.TakenByPictureBefore = true;
                below.ExplainsPictureBefore = true;
                owns |= Take(below, parts, fromEnd: false);
                explained = true;
            }

            if (before is not null && !explained && parts.Sum(part => part.Length) < ShortCaptionCharacters
                && FindContext(content, content.IndexOf(before), block) is { } context)
            {
                parts.Insert(0, context);
            }

            foreach (var credit in credits)
            {
                credit.Absorbed = true;
                credit.TakenByPictureBefore = true;
                parts.Add(credit.Text);
                owns = true;
            }

            var caption = string.Join("\n", parts);
            for (var member = index; member <= end; member++)
            {
                content[member].Caption = caption;
                content[member].Group = group;
                content[member].OwnsText = owns;
            }
        }
    }

    private static void CaptionRow(List<Block> content, HtmlNode row, int group)
    {
        var cells = content.Where(block => ReferenceEquals(block.Row, row)).ToList();
        var words = cells.Where(block => block.Text.Length > 0).ToList();

        var caption = string.Join("\n", words.Select(block => block.Text));
        var owns = caption.Length <= MaxAbsorbedCharacters;
        if (owns)
        {
            foreach (var block in words)
            {
                block.Absorbed = true;
            }
        }
        else
        {
            caption = Head(caption, MaxLentCharacters);
        }

        foreach (var block in cells.Where(block => block.Images.Count > 0))
        {
            block.Caption = caption;
            block.Group = group;
            block.OwnsText = owns;
        }
    }

    /// <summary>The paragraph below a run that says what its pictures show, or null; see <see cref="CaptionPictures"/>.</summary>
    /// <param name="at">The first piece below the run and its credits.</param>
    /// <param name="opensSection">The run is the first thing under its heading, so what follows is about it.</param>
    private static Block? FindBelow(List<Block> content, int at, Block run, Block? before, bool opensSection)
    {
        if (at >= content.Count || !IsPlainText(content[at]) || !SameSection(content[at], run))
        {
            return null;
        }

        var candidate = content[at];
        var introducesNextPicture = at + 1 < content.Count && IsBarePicture(content[at + 1]) && SameSection(content[at + 1], run);

        if (before is null)
        {
            // Captioned from below, unless the line plainly introduces the next picture. Under a heading
            // the paragraph may be as long as it likes; elsewhere only a line reads as a caption.
            if (introducesNextPicture && candidate.Text.EndsWith(':'))
            {
                return null;
            }

            return opensSection || candidate.Text.Length <= MaxCaptionBelowCharacters ? candidate : null;
        }

        return IsLeadIn(before.Text) && !introducesNextPicture
               && (PointsBack(candidate.Text) || SharesAWord(before.Text, candidate.Text))
            ? candidate
            : null;
    }

    /// <summary>
    /// Whether the line is the last of three short ones in a row: a list or a table of contents, whose
    /// last entry is no more a picture's lead-in than its first ("20: General Tips" above a photograph).
    /// </summary>
    private static bool IsListTail(List<Block> content, int index)
    {
        for (var line = index; line > index - 3; line--)
        {
            if (line < 0 || !IsPlainText(content[line]) || !SameSection(content[line], content[index])
                || content[line].Text.Length > MaxListLineLength || EndsSentence(content[line].Text)
                || content[line].Text.EndsWith(':'))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>A credit, as against a bare link or a line without words, which say nothing of a picture either.</summary>
    private static bool IsCredit(string text) =>
        text.Any(char.IsLetter) && !text.Contains("http", StringComparison.OrdinalIgnoreCase) && !text.Contains("www.", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether a paragraph opens by pointing at what stands above it ("These little bumps…", "This bollard
    /// is unique to Utah.") or goes on about it without naming it ("Found throughout the Mojave.").
    /// </summary>
    private static bool PointsBack(string text) => PointingBack().IsMatch(text);

    /// <summary>
    /// Whether two lines speak of the same thing: they share a word of four letters or more, a plural
    /// or other ending aside ("Oklahoma fence" and "Red metallic fences are very common in Oklahoma").
    /// </summary>
    private static bool SharesAWord(string left, string right)
    {
        var words = ContentWords(left);
        return words.Count > 0 && ContentWords(right).Any(word => words.Any(other => SameWord(word, other)));
    }

    private static List<string> ContentWords(string text) =>
        [.. Word().Matches(text.ToLowerInvariant())
            .Select(match => match.Value)
            .Where(word => word.Length >= 4 && !FillerWords.Contains(word))];

    private static bool SameWord(string left, string right)
    {
        var shared = Math.Min(Math.Min(left.Length, right.Length), 5);
        return string.CompareOrdinal(left, 0, right, 0, shared) == 0;
    }

    /// <summary>Words two lines share without sharing a subject.</summary>
    private static readonly HashSet<string> FillerWords = new(StringComparer.Ordinal)
    {
        "photo", "photos", "picture", "pictures", "image", "images", "example", "examples", "with", "from", "that", "this",
        "these", "those", "there", "their", "they", "them", "then", "than", "have", "been", "being", "were", "will", "would",
        "could", "should", "which", "what", "when", "where", "while", "also", "more", "most", "much", "many", "some", "such",
        "very", "like", "into", "onto", "over", "only", "other", "about", "above", "below", "here", "your", "you’ll", "just",
        "found", "find", "seen", "common", "commonly", "often", "usually", "sometimes", "rarely", "mostly", "mainly",
        "coverage", "google", "street", "view", "country", "region", "regions", "area", "areas", "meta", "metas", "guide",
        "north", "south", "east", "west", "northern", "southern", "eastern", "western", "central", "left", "right",
    };

    /// <summary>
    /// Adds a paragraph to a caption: whole, and out of the text chunks, when it is short enough; else
    /// only its nearest part, and it stays a paragraph.
    /// </summary>
    /// <returns>Whether the paragraph now lives in the caption alone.</returns>
    private static bool Take(Block paragraph, List<string> parts, bool fromEnd)
    {
        if (paragraph.Text.Length <= MaxAbsorbedCharacters)
        {
            parts.Add(paragraph.Text);
            paragraph.Absorbed = true;
            return true;
        }

        parts.Add(fromEnd ? Tail(paragraph.Text, MaxLentCharacters) : Head(paragraph.Text, MaxLentCharacters));
        return false;
    }

    /// <summary>
    /// The sentence a short lead-in follows, a few pieces back in the same section: "You got a Gen4 white
    /// car and silver car." for "Ex: Silver car". Lent, not taken: it introduces several pictures.
    /// </summary>
    private static string? FindContext(List<Block> content, int from, Block run)
    {
        for (var index = from - 1; index >= 0 && index >= from - ContextLookBack; index--)
        {
            var candidate = content[index];

            // Another picture's explanation closes what came before it: it is that clue's, not this one's.
            if (!SameSection(candidate, run) || candidate.ExplainsPictureBefore)
            {
                return null;
            }

            if (candidate.Text.Length > 0 && !IsLeadIn(candidate.Text) && !SectionLabel.IsNeverAHeading(candidate.Text))
            {
                return candidate.Text.Length <= MaxLentCharacters ? candidate.Text : Tail(candidate.Text, MaxLentCharacters);
            }
        }

        return null;
    }

    /// <summary>A line that introduces what follows rather than saying something itself: "Black car ex:", "Oklahoma fence ***".</summary>
    private static bool IsLeadIn(string text) =>
        text.EndsWith(':') || (text.Length <= MaxLeadInLength && !EndsSentence(text));

    private static bool EndsSentence(string text)
    {
        var trimmed = text.TrimEnd(')', '"', '”', '\'', '’', ' ');
        return trimmed.Length > 0 && trimmed[^1] is '.' or '!' or '?' or '…';
    }

    private static bool IsBarePicture(Block block) => block.Images.Count > 0 && block.Text.Length == 0;

    private static bool IsPlainText(Block block) => block.Images.Count == 0 && block.Text.Length > 0;

    private static bool SameSection(Block left, Block right) =>
        string.Equals(left.Section, right.Section, StringComparison.Ordinal);

    /// <summary>The start of a paragraph, cut at a sentence's end where one falls in reach.</summary>
    private static string Head(string text, int length)
    {
        if (text.Length <= length)
        {
            return text;
        }

        var cut = text.LastIndexOfAny(['.', '!', '?', '\n'], length - 1);
        return cut >= length / 2 ? text[..(cut + 1)].TrimEnd() : $"{text[..length].TrimEnd()}…";
    }

    /// <summary>The end of a paragraph, from a sentence's start where one falls in reach.</summary>
    private static string Tail(string text, int length)
    {
        if (text.Length <= length)
        {
            return text;
        }

        var from = text.Length - length;
        var cut = text.IndexOfAny(['.', '!', '?', '\n'], from);
        return cut >= 0 && cut < text.Length - length / 2 ? text[(cut + 1)..].TrimStart() : $"…{text[from..].TrimStart()}";
    }

    /// <summary>Whether the block's words are all inside links, arrows and spacing aside.</summary>
    private static bool IsOnlyALink(HtmlNode block, string text)
    {
        var links = block.SelectNodes(".//a[@href]");
        if (links is null)
        {
            return false;
        }

        var linked = string.Concat(links.Select(link => HtmlText.Decode(link.InnerText ?? string.Empty)));
        return LettersAndDigits(linked).Length > 0 && LettersAndDigits(linked) == LettersAndDigits(text);
    }

    private static string LettersAndDigits(string text) => string.Concat(text.Where(char.IsLetterOrDigit));

    /// <summary>
    /// Text as a chunk holds it: no-break spaces — which the documents are full of, and which neither
    /// the model nor a search needs — as plain ones, runs of spaces as one, and no space at either end.
    /// </summary>
    private static string Normalise(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var builder = new System.Text.StringBuilder(text.Length);
        var pendingSpace = false;

        foreach (var character in text)
        {
            if (character is ' ' or ' ' or '\t')
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace && character is not '\n' and not '\r')
            {
                builder.Append(' ');
            }

            pendingSpace = false;
            builder.Append(character);
        }

        return builder.ToString().Trim();
    }

    [GeneratedRegex(@"\p{L}[\p{L}\p{M}'’-]*", RegexOptions.CultureInvariant)]
    private static partial Regex Word();

    [GeneratedRegex(
        @"^[-–•*\s]*(?:this|these|those|it|they|here|above|as\s+(?:you\s+can\s+)?see[ns]?|you\s+can\s+see|note\s+(?:that|how)"
        + @"|the\s+(?:above|picture|image|photo|map)|pictured|shown"
        // A sentence without its subject goes on about the one above: "Found throughout the Mojave."
        + @"|found|seen|(?:(?:most|very|quite|fairly|extremely)\s+)?common(?:ly)?|usually|mostly|mainly|only|also"
        + @"|can\s+(?:(?:also|only)\s+)?be|appears?|occurs?|grows?|located|present|visible|exclusive|unique|rare|typical(?:ly)?)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PointingBack();

    [GeneratedRegex(@"font-weight\s*:\s*(?<weight>[0-9]+|bold(?:er)?)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FontWeight();

    // "4. Oil Palms", "7.21.3 Inspection Stickers", "3) Stickers": a number, perhaps of several parts, then words.
    [GeneratedRegex(@"^(?<number>[0-9]{1,3}(?:\.[0-9]{1,3})*)[.):]?\s+\p{L}", RegexOptions.CultureInvariant)]
    private static partial Regex OutlineNumber();

    // "Chapter 2: Road markers", "Part 3", "Tip 12 -": a word, then a number.
    [GeneratedRegex(@"^(?<word>\p{L}{3,})\s+[0-9]{1,3}\b", RegexOptions.CultureInvariant)]
    private static partial Regex OutlineWord();

    /// <param name="Description">What its author wrote of it, or empty.</param>
    private sealed record Picture(string Source, string Description);

    private sealed class Block(HtmlNode node, string text, IReadOnlyList<Picture> images, HtmlNode? row)
    {
        public HtmlNode Node { get; } = node;

        public string Text { get; } = text;

        public IReadOnlyList<Picture> Images { get; } = images;

        /// <summary>The table row the block stands in, whose words caption its pictures.</summary>
        public HtmlNode? Row { get; } = row;

        public bool IsHeadingElement => HeadingElements.Contains(Node.Name);

        /// <summary>The pictures a heading element held, split from its words.</summary>
        public bool IsPictureOfHeading { get; init; }

        /// <summary>A line of words set in bold, outside any heading element.</summary>
        public bool IsBold { get; init; }

        public bool IsHeading { get; set; }

        public int Level { get; set; }

        public string Section { get; set; } = string.Empty;

        public string? Anchor { get; set; }

        /// <summary>The paragraph lives in a picture's caption, and is no text piece of its own.</summary>
        public bool Absorbed { get; set; }

        /// <summary>The picture before it took the paragraph, for what it shows or as its credit, so the next picture does not.</summary>
        public bool TakenByPictureBefore { get; set; }

        /// <summary>The paragraph says what the picture before it shows, which ends that picture's clue.</summary>
        public bool ExplainsPictureBefore { get; set; }

        public string? Caption { get; set; }

        public int? Group { get; set; }

        public bool OwnsText { get; set; }
    }
}
