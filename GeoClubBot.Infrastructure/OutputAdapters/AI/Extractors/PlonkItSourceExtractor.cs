using System.Globalization;
using System.Text;
using System.Text.Json;
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
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return Error.Unexpected("ai.source_unreachable", $"Could not fetch {source.Url}.");
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

    private static ExtractedDocument Parse(string payload, SourceDescriptor source)
    {
        using var document = JsonDocument.Parse(payload);

        var guide = document.RootElement.GetProperty("data").GetProperty("public");
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

            var sectionPath = string.IsNullOrWhiteSpace(stepTitle)
                ? title ?? source.NaturalKey
                : $"{title ?? source.NaturalKey} > {stepTitle}";

            if (step.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in items.EnumerateArray())
                {
                    var chunk = ReadItem(item, stepIndex, sectionPath, stepTitle, title, source);
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

    private static ExtractedChunk? ReadItem(
        JsonElement item,
        int stepIndex,
        string sectionPath,
        string? stepTitle,
        string? guideTitle,
        SourceDescriptor source)
    {
        if (!item.TryGetProperty("id", out var idElement) || idElement.GetString() is not { } itemId)
        {
            return null;
        }

        // The site's own per-item id: stable across edits, which is what keeps re-ingest idempotent.
        var localKey = $"{stepIndex}/{itemId}";
        var kind = item.TryGetProperty("kind", out var kindElement) ? kindElement.GetString() : null;

        if (string.Equals(kind, "centeredImage", StringComparison.Ordinal))
        {
            var imageUrl = ResolveImageUrl(item.TryGetProperty("imageUrl", out var url) ? url.GetString() : null);
            if (imageUrl is null)
            {
                return null;
            }

            // A standalone image has no caption of its own, so it inherits the section heading as its
            // text. Without that it would carry no text vector at all and be unreachable by a written
            // question — which is how most questions arrive.
            return new ExtractedChunk(
                localKey,
                sectionPath,
                BuildFallbackCaption(guideTitle, stepTitle),
                imageUrl,
                Anchor: itemId);
        }

        if (!item.TryGetProperty("data", out var data))
        {
            return null;
        }

        var text = ReadText(data);
        var tipImageUrl = data.TryGetProperty("image", out var image)
            ? ResolveImageUrl(image.TryGetProperty("imageUrl", out var tipUrl) ? tipUrl.GetString() : null)
            : null;

        if (string.IsNullOrWhiteSpace(text) && tipImageUrl is null)
        {
            return null;
        }

        // A tip with both stays one chunk: the prose is genuinely this image's caption, so keeping
        // them together is what lets a written question reach the picture.
        return new ExtractedChunk(
            localKey,
            sectionPath,
            string.IsNullOrWhiteSpace(text) ? BuildFallbackCaption(guideTitle, stepTitle) : text,
            tipImageUrl,
            Anchor: itemId);
    }

    private static string ReadText(JsonElement data)
    {
        if (!data.TryGetProperty("text", out var text) || text.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var line in text.EnumerateArray())
        {
            var value = line.GetString();
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append("\n\n");
            }

            // Markdown emphasis is left intact; models read it fine and it marks what the guide
            // authors considered important.
            builder.Append(value.Trim());
        }

        return builder.ToString();
    }

    private static string BuildFallbackCaption(string? guideTitle, string? stepTitle) =>
        string.Join(" — ", new[] { guideTitle, stepTitle }.Where(part => !string.IsNullOrWhiteSpace(part)));

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
}
