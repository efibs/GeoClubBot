using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using UseCases.OutputPorts.AI.Ingestion;
using Utilities;

namespace Infrastructure.OutputAdapters.AI.Extractors;

/// <summary>
/// Reads plonkit.net country guides.
///
/// No browser is involved. The page looks empty to a scraper only if you search for post-hydration
/// markup; the complete guide is embedded in the initial response as a JSON payload, with stable
/// per-item ids and an <c>updatedAt</c> that gives change detection for free.
/// </summary>
public sealed partial class PlonkItSourceExtractor(
    IHttpClientFactory httpClientFactory,
    ILogger<PlonkItSourceExtractor> logger) : ISourceExtractor, ISourceCatalog
{
    public const string HttpClientName = "PlonkIt";

    public const string TypeName = "plonkit";

    private static readonly Uri BaseUri = new("https://www.plonkit.net");

    /// <summary>Marks the start of the embedded payload in the initial HTML.</summary>
    private const string PayloadMarker = "{\"success\":true";

    public string SourceType => TypeName;

    public bool CanHandle(Uri url) =>
        url.Host.Equals("plonkit.net", StringComparison.OrdinalIgnoreCase)
        || url.Host.EndsWith(".plonkit.net", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Every page in the sitemap, guide or not. The site's sitemap mixes country guides with
    /// leaderboards, rules and other pages, and a hardcoded exclusion list would rot as the site
    /// changes — so non-guides are identified at extraction time, when the page either carries a
    /// guide payload or does not, and are then recorded as skipped rather than retried.
    /// </summary>
    public async Task<Result<IReadOnlyList<SourceDescriptor>>> ListAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var client = httpClientFactory.CreateClient(HttpClientName);
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
                    TypeName,
                    slug,
                    new Uri(BaseUri, slug),
                    Title: null,
                    Country: slug.Replace('-', ' ')))
                .ToList();

            LogSitemapRead(logger, sources.Count);
            return sources;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Xml.XmlException)
        {
            LogSitemapFailed(logger, ex);
            return Error.Unexpected("ai.plonkit_sitemap_unavailable", "Could not read the PlonkIt sitemap.");
        }
    }

    public async Task<Result<ExtractedDocument>> ExtractAsync(
        SourceDescriptor source,
        CancellationToken cancellationToken = default)
    {
        string html;
        try
        {
            var client = httpClientFactory.CreateClient(HttpClientName);
            html = await client.GetStringAsync(source.Url, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (SourceFetch.IsFetchFailure(ex, cancellationToken))
        {
            return SourceFetch.FromException(ex, "guide page", ForbiddenMeans.HostRefused);
        }

        if (!TryReadPayload(html, out var payload))
        {
            // Not a guide page. Reported as validation rather than an error so the caller records it
            // as skipped and stops retrying it every night.
            return Error.Validation("ai.not_a_guide_page", "This PlonkIt page carries no guide content.");
        }

        try
        {
            return Parse(payload, source);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            LogParseFailed(logger, source.NaturalKey, ex);
            return Error.Unexpected("ai.source_unparsable", $"Could not parse the guide at {source.Url}.");
        }
    }

    private static Result<ExtractedDocument> Parse(string payload, SourceDescriptor source)
    {
        using var document = JsonDocument.Parse(payload);

        // The site embeds a payload on pages that are not guides too: /guide is the index of all of
        // them, and its "data" is an array rather than the guide object. Reported as validation so it
        // is recorded as skipped, rather than failing and being retried — with a warning — every
        // night. Checked by shape rather than caught as an exception, so a genuine guide whose schema
        // changed upstream is still reported as a failure worth looking at.
        if (!document.RootElement.TryGetProperty("data", out var data)
            || data.ValueKind != JsonValueKind.Object
            || !data.TryGetProperty("public", out var guide)
            || guide.ValueKind != JsonValueKind.Object)
        {
            return Error.Validation("ai.not_a_guide_page", "This PlonkIt page carries no guide content.");
        }

        var title = guide.TryGetProperty("title", out var titleElement) ? titleElement.GetString() : source.Title;

        DateTimeOffset? updatedAt = guide.TryGetProperty("updatedAt", out var updatedElement)
            && DateTimeOffset.TryParse(updatedElement.GetString(), CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : null;

        var chunks = new List<ExtractedChunk>();

        if (!guide.TryGetProperty("steps", out var steps) || steps.ValueKind != JsonValueKind.Array)
        {
            return new ExtractedDocument(title, updatedAt, chunks);
        }

        var stepIndex = 0;
        foreach (var step in steps.EnumerateArray())
        {
            var stepTitle = step.TryGetProperty("title", out var stepTitleElement)
                ? stepTitleElement.GetString()
                : null;

            // Headings never carry over from one step to the next: each step opens under its own title.
            var headings = new HeadingTrail();

            if (step.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in items.EnumerateArray())
                {
                    var kind = item.TryGetProperty("kind", out var kindElement) ? kindElement.GetString() : null;
                    if (headings.TryFollow(kind, item))
                    {
                        continue;
                    }

                    var context = new ItemContext(
                        stepIndex, title ?? source.NaturalKey, title, stepTitle, headings.Heading, headings.Subheading);

                    var chunk = ReadItem(item, kind, context);
                    if (chunk is not null)
                    {
                        chunks.Add(chunk);
                    }
                }
            }

            stepIndex++;
        }

        return new ExtractedDocument(title, updatedAt, chunks);
    }

    private static ExtractedChunk? ReadItem(JsonElement item, string? kind, ItemContext context)
    {
        if (!item.TryGetProperty("id", out var idElement) || idElement.GetString() is not { } itemId)
        {
            return null;
        }

        // The site's own per-item id: stable across edits, which is what keeps re-ingest idempotent.
        // Every kind shares the one scheme, so an item the extractor learns to read later simply gains
        // a key of its own and nothing already indexed moves.
        var localKey = $"{context.StepIndex}/{itemId}";

        switch (kind)
        {
            case "centeredImage":
                {
                    var imageUrl = ResolveImageUrl(ReadString(item, "imageUrl"));
                    if (imageUrl is null)
                    {
                        return null;
                    }

                    // A standalone image has no caption of its own, so it is given one. Without that it
                    // would carry no text vector at all and be unreachable by a written question — which
                    // is how most questions arrive.
                    var caption = context.OpensTheGuide ? BuildSummaryCaption(context.Guide) : context.FallbackCaption;
                    return new ExtractedChunk(localKey, context.SectionPath, caption, imageUrl, Anchor: itemId);
                }

            case "centeredImageWithCaption":
                {
                    // The guide's own caption, which the tips' shape keeps under "data" and this one keeps
                    // on the item itself. Measured on 138 guides: 37 such pictures, none of them read before.
                    var imageUrl = ReadImageUrl(item);
                    var caption = ReadText(item);
                    if (imageUrl is null && caption.Length == 0)
                    {
                        return null;
                    }

                    return new ExtractedChunk(
                        localKey,
                        context.SectionPath,
                        caption.Length > 0 ? caption : context.FallbackCaption,
                        imageUrl,
                        Anchor: itemId);
                }

            case "text" or "centeredText" or "notes":
                {
                    // Prose between the tips: what a section covers, what a map's colours mean ("roads
                    // highlighted in red will only have that car"), which clues a neighbour shares. Indexed
                    // as text on its own, because no picture belongs to it.
                    var text = ReadText(item);
                    return text.Length == 0
                        ? null
                        : new ExtractedChunk(localKey, context.SectionPath, text, Anchor: itemId);
                }
        }

        if (!item.TryGetProperty("data", out var data))
        {
            return null;
        }

        var tipText = ReadText(data);
        var tipImageUrl = ReadImageUrl(data);

        if (tipText.Length == 0 && tipImageUrl is null)
        {
            return null;
        }

        // A tip with both stays one chunk: the prose is genuinely this image's caption, so keeping
        // them together is what lets a written question reach the picture.
        return new ExtractedChunk(
            localKey,
            context.SectionPath,
            tipText.Length == 0 ? context.FallbackCaption : tipText,
            tipImageUrl,
            Anchor: itemId);
    }

    /// <summary>
    /// The text of a tip's data or of a prose item. The payload stores it as an array of paragraphs on
    /// tips, text and notes items, and as one string on centred text and picture captions.
    /// </summary>
    private static string ReadText(JsonElement owner)
    {
        if (!owner.TryGetProperty("text", out var text))
        {
            return string.Empty;
        }

        IEnumerable<string?> paragraphs = text.ValueKind switch
        {
            JsonValueKind.Array => text.EnumerateArray()
                .Select(line => line.ValueKind == JsonValueKind.String ? line.GetString() : null),
            JsonValueKind.String => [text.GetString()],
            _ => []
        };

        var builder = new StringBuilder();
        foreach (var value in paragraphs)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append("\n\n");
            }

            // Markdown emphasis is left intact; models read it fine and it marks what the guide
            // authors considered important. Links are reduced to their text (see MarkdownLink).
            builder.Append(MarkdownLink().Replace(value.Trim(), "$1"));
        }

        return builder.ToString();
    }

    /// <summary>The picture of a tip's data or of a captioned picture: both nest it under "image".</summary>
    private static string? ReadImageUrl(JsonElement owner) =>
        owner.TryGetProperty("image", out var image) && image.ValueKind == JsonValueKind.Object
            ? ResolveImageUrl(ReadString(image, "imageUrl"))
            : null;

    private static string? ReadString(JsonElement owner, string property) =>
        owner.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>
    /// A link's target, unlike its text, says nothing a question would: a quarter of the guide's items
    /// link example locations and species pages, and in those the URLs are a fifth of the characters —
    /// "[White car long antenna](https://maps.app.goo.gl/szUNXBWMYPjJ6jMA6) is found in…" carries five.
    /// Measured against the embedding model, dropping the targets raised similarity to the question
    /// each answers by 0.04 to 0.06, enough to lift "white car long antenna" from 39th to about 8th.
    /// Nothing downstream needs them: the model is told never to write a URL, and every excerpt is
    /// already credited with a link to its guide.
    /// </summary>
    [GeneratedRegex(@"\[([^\]]+)\]\(\s*https?://[^)\s]+\s*\)", RegexOptions.CultureInvariant)]
    private static partial Regex MarkdownLink();

    /// <summary>The "Step 1.2 -" a heading may open with.</summary>
    [GeneratedRegex(@"^\s*step\s*\d+(?:\.\d+)*\s*[-–—:.]?\s*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex StepNumber();

    /// <summary>
    /// The caption of the picture a country guide opens with: a summary infographic of the whole guide,
    /// in 137 of the 138 guides measured (all but the beginners' guide). It used to be captioned with
    /// nothing but the guide and step titles ("Tunisia — Identifying Tunisia"), which only repeats the
    /// header every chunk of the guide already carries and says nothing about what the picture is — so
    /// a question asking for an overview had nothing to match. Worded to say what the picture is without
    /// naming a clue type: the keyword topic classifier would otherwise label an overview of every clue
    /// with whichever single topic it happened to match first.
    /// </summary>
    private static string BuildSummaryCaption(string guide) =>
        $"{guide} summary infographic: an overview of the most important clues in the guide, shown together in one picture.";

    /// <summary>Image paths in the payload are site-relative.</summary>
    private static string? ResolveImageUrl(string? imageUrl) =>
        string.IsNullOrWhiteSpace(imageUrl) ? null : new Uri(BaseUri, imageUrl).ToString();

    /// <summary>
    /// Finds the embedded payload by brace-matching from its marker, tracking string state so a brace
    /// inside guide prose cannot end the object early.
    /// </summary>
    private static bool TryReadPayload(string html, out string payload)
    {
        payload = string.Empty;

        var start = html.IndexOf(PayloadMarker, StringComparison.Ordinal);
        if (start < 0)
        {
            return false;
        }

        var depth = 0;
        var inString = false;
        var escaped = false;

        for (var index = start; index < html.Length; index++)
        {
            var character = html[index];

            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (character == '\\' && inString)
            {
                escaped = true;
                continue;
            }

            if (character == '"')
            {
                inString = !inString;
                continue;
            }

            if (inString)
            {
                continue;
            }

            if (character == '{')
            {
                depth++;
            }
            else if (character == '}' && --depth == 0)
            {
                payload = html[start..(index + 1)];
                return true;
            }
        }

        return false;
    }

    [LoggerMessage(LogLevel.Information, "Read {PageCount} page(s) from the PlonkIt sitemap.")]
    static partial void LogSitemapRead(ILogger logger, int pageCount);

    [LoggerMessage(LogLevel.Warning, "Could not read the PlonkIt sitemap.")]
    static partial void LogSitemapFailed(ILogger logger, Exception exception);

    [LoggerMessage(LogLevel.Warning, "Could not parse the PlonkIt guide {Slug}.")]
    static partial void LogParseFailed(ILogger logger, string slug, Exception exception);

    /// <summary>Where an item sits in the guide: what its section path and any fallback caption are made of.</summary>
    /// <param name="Guide">The guide's title, or its slug when the payload names none.</param>
    /// <param name="GuideTitle">The guide's title as published; captions never fall back to the slug.</param>
    private sealed record ItemContext(
        int StepIndex,
        string Guide,
        string? GuideTitle,
        string? StepTitle,
        string? Heading,
        string? Subheading)
    {
        public string SectionPath => JoinPresent(" > ", Guide, StepTitle, Heading, Subheading);

        public string FallbackCaption => JoinPresent(" — ", GuideTitle, StepTitle, Heading, Subheading);

        /// <summary>The first step, before any heading: where every guide puts its summary infographic.</summary>
        public bool OpensTheGuide => StepIndex == 0 && Heading is null && Subheading is null;

        private static string JoinPresent(string separator, params string?[] parts) =>
            string.Join(separator, parts.Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    /// <summary>
    /// The divider and subsection headings in effect at the current item of a step.
    ///
    /// They are the guide's topical headings — "Car metas", "Bollards", "Landscape" — and were ignored
    /// until measured: 1,892 of 5,276 tips (36%) sit under one, and a source's own topical heading is the
    /// largest single influence on retrieval that the embedding header has (see EmbeddingTextBuilder).
    ///
    /// The guides nest the two kinds both ways round. Kyrgyzstan puts dividers ("EM-04") beneath a
    /// subsection ("Roads"); the United States puts subsections ("Poles") beneath a divider
    /// ("Infrastructure"). So whichever kind a step uses first is taken as its outer level and the other
    /// as the level beneath it. Of the 473 steps in the 138 guides measured, 6 use both kinds; every
    /// other step uses one or none, and one kind is simply a single level. An untitled heading ends the
    /// one before it.
    /// </summary>
    private sealed class HeadingTrail
    {
        private string? _outerKind;

        public string? Heading { get; private set; }

        public string? Subheading { get; private set; }

        /// <summary>Takes in the item when it is a heading, and says so, so it is not read as content.</summary>
        public bool TryFollow(string? kind, JsonElement item)
        {
            if (kind is not ("divider" or "subsection"))
            {
                return false;
            }

            // "Step 1.2 - US & Canada" is how a few guides number the parts of their first step (17 of
            // the 272 headings measured). The number is position, not subject — the kind of filler the
            // embedding header measurably loses by — so only "US & Canada" is kept.
            var title = StepNumber().Replace(ReadString(item, "title") ?? string.Empty, string.Empty).Trim();
            if (title.Length == 0)
            {
                title = null;
            }

            _outerKind ??= kind;

            if (string.Equals(kind, _outerKind, StringComparison.Ordinal))
            {
                Heading = title;
                Subheading = null;
            }
            else
            {
                Subheading = title;
            }

            return true;
        }
    }
}
