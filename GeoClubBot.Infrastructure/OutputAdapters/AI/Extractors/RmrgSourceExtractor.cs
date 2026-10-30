using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using UseCases.OutputPorts.AI.Ingestion;
using UseCases.UseCases.AI.Ingestion;
using Utilities;

namespace Infrastructure.OutputAdapters.AI.Extractors;

/// <summary>
/// Reads rmrg.me country guides — the Real Meta Research Group's regionguessing guides.
///
/// The site is server-rendered, so one plain GET returns the whole guide; no browser and no embedded
/// payload hunt. Every clue is a single <c>meta-item</c>: one picture and the prose describing it,
/// beneath a category and optional subcategory heading, carrying an id that doubles as the page's own
/// share anchor. That is the shape retrieval wants — a written question reaches the picture through
/// its caption — and the id keeps both the deep link and the point id stable across re-extraction.
///
/// Fewer countries than plonkit.net and far more detail per country, so the two complement rather
/// than duplicate each other; a source listed by both is deduplicated by the catalogue sync.
/// </summary>
public sealed partial class RmrgSourceExtractor(
    IHttpClientFactory httpClientFactory,
    ILogger<RmrgSourceExtractor> logger) : ISourceExtractor, ISourceCatalog
{
    private static readonly Uri BaseUri = new("https://rmrg.me/");

    /// <summary>
    /// XPath class tests are written the long way — a bare <c>contains(@class, 'x')</c> also matches
    /// <c>xyz</c>, and this markup has several classes sharing a prefix.
    ///
    /// The element is left open. A redesign of the site (live by 23 October 2026, absent from the August
    /// capture) moved its items from <c>div</c> to <c>article</c>, its headings from <c>div</c> to
    /// <c>section</c> and its title from <c>h2</c> to <c>h1</c>. Matching only the old element made every
    /// guide read as having no items, which records it as skipped — permanently. The class is what both
    /// versions agree on.
    /// </summary>
    private const string ItemPath = "//*[contains(concat(' ', normalize-space(@class), ' '), ' meta-item ')][@id]";

    private const string TitlePath =
        "//*[self::h1 or self::h2][contains(concat(' ', normalize-space(@class), ' '), ' country-title ')]";

    private const string UpdatedPath =
        "//*[contains(concat(' ', normalize-space(@class), ' '), ' contributors-updated ')]";

    private const string DescriptionPath =
        ".//div[contains(concat(' ', normalize-space(@class), ' '), ' meta-description ')]";

    private const string ImagePath =
        ".//div[contains(concat(' ', normalize-space(@class), ' '), ' meta-image-wrapper ')]//img";

    public string SourceType => SourceLinkClassifier.Rmrg;

    public bool CanHandle(Uri url) =>
        url.Host.Equals("rmrg.me", StringComparison.OrdinalIgnoreCase)
        || url.Host.EndsWith(".rmrg.me", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Every top-level page in the sitemap, guide or not.
    ///
    /// The sitemap mixes the guides with the landing page and a daily challenge, and a hardcoded
    /// exclusion list would rot as the site grows. So non-guides are identified at extraction time,
    /// where a page either carries guide items or does not, and are then recorded as skipped rather
    /// than retried nightly — the same rule the other site catalogue follows.
    /// </summary>
    public async Task<Result<IReadOnlyList<SourceDescriptor>>> ListAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var client = httpClientFactory.CreateClient(PlonkItSourceExtractor.HttpClientName);
            var sitemap = await client.GetStringAsync(new Uri(BaseUri, "sitemap.xml"), cancellationToken)
                .ConfigureAwait(false);

            var document = XDocument.Parse(sitemap);

            var sources = document.Descendants()
                .Where(element => element.Name.LocalName == "loc")
                .Select(element => element.Value.Trim())
                .Select(location => Uri.TryCreate(location, UriKind.Absolute, out var uri) ? uri : null)
                .Where(uri => uri is not null)
                .Select(uri => uri!.AbsolutePath.Trim('/'))
                .Where(slug => slug.Length > 0 && !slug.Contains('/'))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(slug => new SourceDescriptor(
                    SourceLinkClassifier.Rmrg,
                    slug,
                    // Trailing slash kept: without it the site answers 301, and every citation would
                    // then point at a redirect rather than at the guide.
                    new Uri(BaseUri, $"{slug}/"),
                    Title: null,
                    Country: slug.Replace('-', ' ')))
                .ToList();

            LogSitemapRead(logger, sources.Count);
            return sources;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Xml.XmlException)
        {
            LogSitemapFailed(logger, ex);
            return Error.Unexpected("ai.rmrg_sitemap_unavailable", "Could not read the RMRG sitemap.");
        }
    }

    public async Task<Result<ExtractedDocument>> ExtractAsync(
        SourceDescriptor source,
        CancellationToken cancellationToken = default)
    {
        string html;
        try
        {
            var client = httpClientFactory.CreateClient(PlonkItSourceExtractor.HttpClientName);
            html = await client.GetStringAsync(source.Url, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (SourceFetch.IsFetchFailure(ex, cancellationToken))
        {
            return SourceFetch.FromException(ex, "guide page", ForbiddenMeans.HostRefused);
        }

        var document = new HtmlDocument();
        document.LoadHtml(html);

        var items = document.DocumentNode.SelectNodes(ItemPath);
        if (items is null || items.Count == 0)
        {
            // The landing page and the daily challenge live in the same sitemap. Reported as a
            // validation failure so the caller records them as skipped instead of retrying them.
            return Error.Validation("ai.not_a_guide_page", "This RMRG page carries no guide content.");
        }

        var title = ReadTitle(document) ?? source.Title ?? source.NaturalKey;
        var updatedAt = ReadUpdatedAt(document);

        var chunks = items
            .Select(item => ReadItem(item, title, updatedAt))
            .OfType<ExtractedChunk>()
            .ToList();

        LogGuideRead(logger, source.NaturalKey, chunks.Count);
        return new ExtractedDocument(title, updatedAt, chunks);
    }

    private static ExtractedChunk? ReadItem(HtmlNode item, string title, DateTimeOffset? guideUpdatedAt)
    {
        // The site's own item id: it is both the share anchor and a path of slugs the authors chose,
        // so it survives edits and reordering. A positional key would turn every later chunk into a
        // duplicate the moment the guide gains an item. Decoded like every other attribute here: an
        // encoded character would otherwise make both the point id and the deep link name something
        // the page does not contain.
        var itemId = HtmlText.Decode(item.GetAttributeValue("id", string.Empty));
        if (string.IsNullOrWhiteSpace(itemId))
        {
            return null;
        }

        var description = item.SelectSingleNode(DescriptionPath);
        var text = description is null ? string.Empty : ReadDescription(description);
        var imageUrl = ReadImageUrl(item, guideUpdatedAt);

        if (text.Length == 0 && imageUrl is null)
        {
            return null;
        }

        var sectionPath = BuildSectionPath(item, title);

        // An item with no slug of its own — the introduction to a subcategory — has carried an id ending
        // in "/" since the redesign, and none before it. Trimmed for the key alone, so the
        // point already indexed for it is updated in place; the anchor keeps the id the page carries,
        // because that is what a deep link has to name.
        var localKey = itemId.TrimEnd('/');

        return new ExtractedChunk(
            localKey.Length > 0 ? localKey : itemId,
            sectionPath,
            // An image with no prose of its own inherits its headings, or it would carry no text
            // vector at all and be unreachable by a written question — which is how most arrive.
            text.Length > 0 ? text : sectionPath,
            imageUrl,
            Anchor: itemId);
    }

    /// <summary>
    /// Headings come from the enclosing sections, which is where the page itself puts them, and end
    /// with the item's own title where the authors wrote one.
    /// </summary>
    private static string BuildSectionPath(HtmlNode item, string title)
    {
        var category = ReadHeading(item, "category-section", "h3");
        var subcategory = ReadHeading(item, "subcategory-section", "h4");
        var itemTitle = ReadItemTitle(item, category, subcategory);

        var segments = new List<string>();
        foreach (var segment in new[] { title, category, subcategory, itemTitle })
        {
            // Some guides repeat a category as its own first subcategory ("Architecture > Architecture"),
            // which labels nothing twice.
            if (!string.IsNullOrWhiteSpace(segment)
                && (segments.Count == 0 || !string.Equals(segments[^1], segment, StringComparison.OrdinalIgnoreCase)))
            {
                segments.Add(segment);
            }
        }

        return string.Join(" > ", segments);
    }

    private static string? ReadHeading(HtmlNode item, string sectionClass, string headingElement)
    {
        var section = item.Ancestors()
            .FirstOrDefault(node => node.Name is "div" or "section" && node.HasClass(sectionClass));
        var heading = section?.SelectSingleNode($".//{headingElement}");

        return heading is null ? null : Normalise(HtmlText.Decode(heading.InnerText));
    }

    /// <summary>
    /// The item's <c>data-title</c>, when it is a heading rather than a file name.
    ///
    /// The titles name the clue as the page's own search box knows it — "Distribution of Flat versus
    /// Round Bollards", "Sumatran Pine", "Kayseri" — and a topical heading is the largest single
    /// influence on retrieval the embedding header has (see EmbeddingTextBuilder). Many items carry
    /// their slug there instead ("g-topography", "l-säntis"), which says nothing a heading should, and
    /// some only repeat the heading above them ("Roof Decoration 6" under "Roof Decoration"); both are
    /// left out. Measured on the 13 live guides: 1,007 of the 1,313 items read gain a heading this way.
    /// </summary>
    private static string? ReadItemTitle(HtmlNode item, params string?[] enclosingHeadings)
    {
        var title = Normalise(HtmlText.Decode(item.GetAttributeValue("data-title", string.Empty)));
        var slug = HtmlText.Decode(item.GetAttributeValue("data-item-slug", string.Empty)).Trim();

        if (title.Length == 0 || IsSlugLike(title, slug))
        {
            return null;
        }

        var unnumbered = TrailingNumber().Replace(title, string.Empty);
        return enclosingHeadings.Any(heading =>
            string.Equals(title, heading, StringComparison.OrdinalIgnoreCase)
            || string.Equals(unnumbered, heading, StringComparison.OrdinalIgnoreCase))
            ? null
            : title;
    }

    /// <summary>
    /// A title is a file name when it is exactly the item's slug, lower-case words joined by hyphens
    /// ("g-topography", "l-säntis", "I-bus-stops" with its one-letter prefix), or no word at all
    /// ("***"). Capitals or a space make it the authors' own: "Kayseri" over the slug "kayseri",
    /// "TUK-TUKS", "-ówko", "Ślęża" over "l-a".
    /// </summary>
    private static bool IsSlugLike(string title, string slug) =>
        string.Equals(title, slug, StringComparison.Ordinal)
        || !title.Any(char.IsLetter)
        || FileName().IsMatch(title);

    private static string? ReadTitle(HtmlDocument document)
    {
        var heading = document.DocumentNode.SelectSingleNode(TitlePath);
        if (heading is null)
        {
            return null;
        }

        var title = Normalise(HtmlText.Decode(heading.InnerText));
        return title.Length > 0 ? title : null;
    }

    /// <summary>
    /// Reads the guide's own "updated at" stamp. Cheaper and truer than hashing the body: the markup
    /// carries cache-busting version numbers that churn without the content changing.
    /// </summary>
    private static DateTimeOffset? ReadUpdatedAt(HtmlDocument document)
    {
        var stamp = document.DocumentNode.SelectSingleNode(UpdatedPath);
        if (stamp is null)
        {
            return null;
        }

        var match = UpdatedAt().Match(HtmlText.Decode(stamp.InnerText));
        if (!match.Success)
        {
            return null;
        }

        return DateTime.TryParseExact(
            match.Groups["date"].Value, "d MMMM yyyy", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var date)
            ? new DateTimeOffset(date, TimeSpan.Zero)
            : null;
    }

    /// <summary>
    /// The picture beneath the annotations.
    ///
    /// An annotated item stacks a transparent SVG of arrows and circles over a photo. The overlay
    /// alone shows nothing, and compositing the two would mean rasterising SVG, so the photo is what
    /// gets indexed — the prose already says what the arrows point at.
    ///
    /// The optimised copy is taken over the original deliberately: they are the same picture, and
    /// originals here reach several megabytes, which the AI provider would then fetch server-side on
    /// every embedding call. Since the redesign the optimised copy is the plain
    /// <c>src</c> and the original sits in <c>data-high-src</c>, which is never read.
    ///
    /// Vector sources are passed over wherever they turn up. Some items here are illustrations rather
    /// than photographs and the site serves those as <c>.svgz</c> from every attribute, overlay class
    /// or not; the AI provider answers 415 for them and the whole request they were batched into fails
    /// with them. Such an item is indexed on its prose alone.
    /// </summary>
    private static string? ReadImageUrl(HtmlNode item, DateTimeOffset? guideUpdatedAt)
    {
        var images = item.SelectNodes(ImagePath);
        if (images is null)
        {
            return null;
        }

        foreach (var image in images)
        {
            if (image.HasClass("svg-overlay"))
            {
                continue;
            }

            var source = new[] { "data-optimized-src", "src", "data-original-src" }
                // Attribute values arrive HTML-encoded, and these paths contain the characters that
                // get encoded: one category here is "linguistic & culture". Left as "&amp;" the URL
                // names a path that does not exist, and every picture under it 404s — for the
                // embedding provider and for Discord alike.
                .Select(attribute => HtmlText.Decode(image.GetAttributeValue(attribute, string.Empty)))
                .Where(value => !string.IsNullOrWhiteSpace(value))
                // Site-relative in the markup; the provider fetches these itself, so it has to be
                // absolute — and escaped, because the same category puts spaces in the path and
                // ToString() would hand out the display form of the URL rather than a fetchable one.
                .Select(value => new Uri(BaseUri, value).AbsoluteUri)
                .FirstOrDefault(ImageFormats.CanEmbed);

            if (source is not null)
            {
                return WithoutRelease(source, guideUpdatedAt);
            }
        }

        return null;
    }

    /// <summary>
    /// The picture under the site's own path rather than under the release it is served from today.
    ///
    /// Since the redesign every picture is served from <c>assets.rmrg.me/r/{release}/…</c>, and that
    /// release token belongs to the whole site, not to a guide or a picture: a guide last updated in
    /// July carries the same one as a guide updated in October. The image URL is part of each source's
    /// change hash, so stored as served, an edit to any one guide would re-embed every guide's text and
    /// pictures at the next re-check. And the asset host answers 404 for a token it does not know, so
    /// once an old release is retired, every picture stored under it would stop loading.
    ///
    /// The path the site used before the redesign, <c>rmrg.me/guides/{path}</c>, is still served and
    /// redirects (302) to the current release, so that path is what gets stored: it stays the same as
    /// long as the picture does, and always reaches today's copy.
    ///
    /// The old markup gave each picture its own version, so a picture replaced under the same file name
    /// still changed its URL. The redesign has no such version, so the guide's own update stamp is
    /// used instead. It changes only when this guide does, and the redirect ignores it.
    /// </summary>
    private static string WithoutRelease(string imageUrl, DateTimeOffset? guideUpdatedAt)
    {
        var match = ReleaseAsset().Match(imageUrl);
        if (!match.Success)
        {
            return imageUrl;
        }

        var version = guideUpdatedAt is { } updatedAt
            ? "?v=" + updatedAt.UtcDateTime.ToString("yyyyMMdd", CultureInfo.InvariantCulture)
            : string.Empty;

        // The path arrives escaped already ("linguistic%20%26%20culture"), and Uri leaves valid escapes
        // as they are, so nothing is escaped twice.
        return new Uri(BaseUri, $"guides/{match.Groups["path"].Value}{version}").AbsoluteUri;
    }

    private static string ReadDescription(HtmlNode description)
    {
        var builder = new StringBuilder();
        AppendText(description, builder);

        return Normalise(builder.ToString());
    }

    /// <summary>
    /// Flattens the description to plain text, keeping the breaks that carry meaning.
    ///
    /// <c>InnerText</c> alone would run a bulleted list of distinct clues into one sentence. The
    /// authors mark their paragraph breaks with an empty span, so those have to be honoured too.
    /// </summary>
    private static void AppendText(HtmlNode node, StringBuilder builder)
    {
        foreach (var child in node.ChildNodes)
        {
            if (child.NodeType == HtmlNodeType.Text)
            {
                builder.Append(HtmlText.Decode(child.InnerText));
                continue;
            }

            if (child.NodeType != HtmlNodeType.Element)
            {
                continue;
            }

            switch (child.Name)
            {
                case "br":
                    builder.Append('\n');
                    break;

                case "li":
                    builder.Append("\n- ");
                    AppendText(child, builder);
                    break;

                case "ul" or "ol" or "p" or "aside" or "div":
                    builder.Append('\n');
                    AppendText(child, builder);
                    builder.Append('\n');
                    break;

                default:
                    if (child.HasClass("markdown-blank-line"))
                    {
                        builder.Append("\n\n");
                        break;
                    }

                    AppendText(child, builder);
                    break;
            }
        }
    }

    /// <summary>Squeezes the whitespace the markup adds without losing the structure it encodes.</summary>
    private static string Normalise(string text)
    {
        var collapsed = LineBreak().Replace(text, "\n");
        collapsed = BlankLines().Replace(collapsed, "\n\n");

        return RepeatedSpaces().Replace(collapsed, " ").Trim();
    }

    /// <summary>"Guide updated at 29 August 2026" before the redesign, "Updated 23 October 2026" since.</summary>
    [GeneratedRegex(@"updated(?:\s+at)?\s+(?<date>\d{1,2}\s+\w+\s+\d{4})",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UpdatedAt();

    /// <summary>A picture on the asset host, under a release token: <c>https://assets.rmrg.me/r/{release}/{path}</c>.</summary>
    [GeneratedRegex(@"^https://assets\.rmrg\.me/r/[^/?#]+/(?<path>[^?#]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ReleaseAsset();

    /// <summary>Lower-case words joined by hyphens or underscores, the first of them allowed to be one letter of either case.</summary>
    [GeneratedRegex(@"^(?:\p{L}|\p{Ll}[\p{Ll}\p{Nd}]*)(?:[-_][\p{Ll}\p{Nd}]+)+$", RegexOptions.CultureInvariant)]
    private static partial Regex FileName();

    /// <summary>The number that tells apart several items filed under the same name: "Roof Decoration 6".</summary>
    [GeneratedRegex(@"\s*\d+$", RegexOptions.CultureInvariant)]
    private static partial Regex TrailingNumber();

    [GeneratedRegex(@"[ \t]*\n[ \t]*", RegexOptions.CultureInvariant)]
    private static partial Regex LineBreak();

    [GeneratedRegex(@"\n{3,}", RegexOptions.CultureInvariant)]
    private static partial Regex BlankLines();

    [GeneratedRegex(@"[ \t]{2,}", RegexOptions.CultureInvariant)]
    private static partial Regex RepeatedSpaces();

    [LoggerMessage(LogLevel.Information, "Read {PageCount} page(s) from the RMRG sitemap.")]
    static partial void LogSitemapRead(ILogger logger, int pageCount);

    [LoggerMessage(LogLevel.Warning, "Could not read the RMRG sitemap.")]
    static partial void LogSitemapFailed(ILogger logger, Exception exception);

    [LoggerMessage(LogLevel.Debug, "Read {ChunkCount} item(s) from the RMRG guide {Slug}.")]
    static partial void LogGuideRead(ILogger logger, string slug, int chunkCount);
}
