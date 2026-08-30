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
    /// </summary>
    private const string ItemPath = "//div[contains(concat(' ', normalize-space(@class), ' '), ' meta-item ')][@id]";

    private const string TitlePath = "//h2[contains(concat(' ', normalize-space(@class), ' '), ' country-title ')]";

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
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return Error.Unexpected("ai.source_unreachable", $"Could not fetch {source.Url}.");
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

        var chunks = items
            .Select(item => ReadItem(item, title))
            .OfType<ExtractedChunk>()
            .ToList();

        LogGuideRead(logger, source.NaturalKey, chunks.Count);
        return new ExtractedDocument(title, ReadUpdatedAt(document), chunks);
    }

    private static ExtractedChunk? ReadItem(HtmlNode item, string title)
    {
        // The site's own item id: it is both the share anchor and a path of slugs the authors chose,
        // so it survives edits and reordering. A positional key would turn every later chunk into a
        // duplicate the moment the guide gains an item.
        var itemId = item.GetAttributeValue("id", string.Empty);
        if (string.IsNullOrWhiteSpace(itemId))
        {
            return null;
        }

        var description = item.SelectSingleNode(DescriptionPath);
        var text = description is null ? string.Empty : ReadDescription(description);
        var imageUrl = ReadImageUrl(item);

        if (text.Length == 0 && imageUrl is null)
        {
            return null;
        }

        var sectionPath = BuildSectionPath(item, title);

        return new ExtractedChunk(
            itemId,
            sectionPath,
            // An image with no prose of its own inherits its headings, or it would carry no text
            // vector at all and be unreachable by a written question — which is how most arrive.
            text.Length > 0 ? text : sectionPath,
            imageUrl,
            Anchor: itemId);
    }

    /// <summary>Headings come from the enclosing sections, which is where the page itself puts them.</summary>
    private static string BuildSectionPath(HtmlNode item, string title)
    {
        var category = ReadHeading(item, "category-section", "h3");
        var subcategory = ReadHeading(item, "subcategory-section", "h4");

        return string.Join(" > ", new[] { title, category, subcategory }
            .Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    private static string? ReadHeading(HtmlNode item, string sectionClass, string headingElement)
    {
        var section = item.Ancestors("div").FirstOrDefault(node => node.HasClass(sectionClass));
        var heading = section?.SelectSingleNode($".//{headingElement}");

        return heading is null ? null : Normalise(HtmlEntity.DeEntitize(heading.InnerText));
    }

    private static string? ReadTitle(HtmlDocument document)
    {
        var heading = document.DocumentNode.SelectSingleNode(TitlePath);
        if (heading is null)
        {
            return null;
        }

        var title = Normalise(HtmlEntity.DeEntitize(heading.InnerText));
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

        var match = UpdatedAt().Match(HtmlEntity.DeEntitize(stamp.InnerText));
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
    /// every embedding call.
    /// </summary>
    private static string? ReadImageUrl(HtmlNode item)
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
                .Select(attribute => image.GetAttributeValue(attribute, string.Empty))
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

            if (source is not null)
            {
                // Site-relative in the markup; the provider fetches these itself, so it has to be absolute.
                return new Uri(BaseUri, source).ToString();
            }
        }

        return null;
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
                builder.Append(HtmlEntity.DeEntitize(child.InnerText));
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

    [GeneratedRegex(@"updated at\s+(?<date>\d{1,2}\s+\w+\s+\d{4})",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UpdatedAt();

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
