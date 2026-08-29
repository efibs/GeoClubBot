using System.IO.Compression;
using Configuration;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.OutputPorts.AI;
using UseCases.OutputPorts.AI.Ingestion;
using UseCases.UseCases.AI.Ingestion;
using Utilities;

namespace Infrastructure.OutputAdapters.AI.Extractors;

/// <summary>
/// Reads publicly shared Google Docs, which make up the largest share of the guide library.
///
/// Two export formats, chosen by whether images can be served at all:
///
/// - **Text** (~2 KB) when images are not wanted. Cheap, and everything the model reads.
/// - **Zip** (~1.5 MB) when they are: the same document as HTML plus its images as separate files.
///
/// The HTML export is never used — it inlines every image as base64, costing the same bandwidth as
/// the zip while making the images harder to pull out. Note the size gap is almost entirely images:
/// the HTML inside the zip is around 10 KB.
/// </summary>
public sealed partial class GoogleDocSourceExtractor(
    IHttpClientFactory httpClientFactory,
    IImageRelay imageRelay,
    IOptions<AiIngestionConfiguration> ingestionConfiguration,
    ILogger<GoogleDocSourceExtractor> logger) : ISourceExtractor
{
    /// <summary>A line this short, standing alone between blanks, is treated as a heading.</summary>
    private const int MaxHeadingLength = 80;

    private static readonly string[] BlockElements = ["p", "h1", "h2", "h3", "h4", "h5", "h6", "li"];

    public string SourceType => SourceLinkClassifier.GoogleDoc;

    public bool CanHandle(Uri url) =>
        SourceLinkClassifier.Classify(url).SourceType == SourceLinkClassifier.GoogleDoc;

    public async Task<Result<ExtractedDocument>> ExtractAsync(
        SourceDescriptor source,
        CancellationToken cancellationToken = default)
    {
        // Fetching the heavier export is only worth it if the images can actually be served
        // afterwards; without a relay their bytes have nowhere to live.
        var wantsImages = imageRelay.IsEnabled && ingestionConfiguration.Value.EmbedImages;

        var format = wantsImages ? "zip" : "txt";
        var endpoint = new Uri($"https://docs.google.com/document/d/{source.NaturalKey}/export?format={format}");

        byte[] payload;
        try
        {
            var client = httpClientFactory.CreateClient(PlonkItSourceExtractor.HttpClientName);
            using var response = await client.GetAsync(endpoint, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                // A document that is not publicly shared answers 401/403 and will keep doing so, so it
                // is reported as validation and recorded as skipped rather than retried nightly.
                return (int)response.StatusCode is 401 or 403 or 404
                    ? Error.Validation("ai.document_not_public",
                        $"This document is not publicly readable (HTTP {(int)response.StatusCode}).")
                    : Error.Unexpected("ai.source_unreachable",
                        $"Could not fetch the document (HTTP {(int)response.StatusCode}).");
            }

            payload = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            LogFetchFailed(logger, source.NaturalKey, ex);
            return Error.Unexpected("ai.source_unreachable", $"Could not fetch the document {source.NaturalKey}.");
        }

        var chunks = wantsImages
            ? await ReadArchiveAsync(payload, source, cancellationToken).ConfigureAwait(false)
            : ParseText(System.Text.Encoding.UTF8.GetString(payload), source);

        return chunks.Count == 0
            ? Error.Validation("ai.document_empty", "This document has no readable text.")
            : new ExtractedDocument(source.Title, SourceUpdatedAtUtc: null, chunks);
    }

    /// <summary>
    /// Reads the zip export: the document's HTML plus its images as separate entries, so each image
    /// can be stored and captioned with the text it sits in.
    /// </summary>
    private async Task<List<ExtractedChunk>> ReadArchiveAsync(
        byte[] payload,
        SourceDescriptor source,
        CancellationToken cancellationToken)
    {
        try
        {
            using var archive = new ZipArchive(new MemoryStream(payload), ZipArchiveMode.Read);

            var htmlEntry = archive.Entries.FirstOrDefault(entry =>
                entry.FullName.EndsWith(".html", StringComparison.OrdinalIgnoreCase));

            if (htmlEntry is null)
            {
                return [];
            }

            using var reader = new StreamReader(htmlEntry.Open());
            var html = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);

            return await ParseHtmlAsync(html, archive, source, cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidDataException ex)
        {
            LogArchiveUnreadable(logger, source.NaturalKey, ex);
            return [];
        }
    }

    private async Task<List<ExtractedChunk>> ParseHtmlAsync(
        string html,
        ZipArchive archive,
        SourceDescriptor source,
        CancellationToken cancellationToken)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html);

        var blocks = document.DocumentNode.SelectNodes(
            string.Join(" | ", BlockElements.Select(element => $"//body//{element}")));

        var chunks = new List<ExtractedChunk>();
        var section = DefaultSection(source);
        var textIndex = 0;
        var imageIndex = 0;

        // The last non-empty text, so an image in a paragraph of its own still gets a caption.
        var lastText = string.Empty;

        foreach (var block in blocks ?? Enumerable.Empty<HtmlNode>())
        {
            var text = HtmlEntity.DeEntitize(block.InnerText ?? string.Empty).Trim();
            var images = block.SelectNodes(".//img") ?? Enumerable.Empty<HtmlNode>();

            if (text.Length > 0)
            {
                if (LooksLikeHeading(text))
                {
                    section = $"{DefaultSection(source)} > {text}";
                }
                else
                {
                    chunks.Add(new ExtractedChunk($"p{textIndex++}", section, text));
                    lastText = text;
                }
            }

            foreach (var image in images)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var stored = await StoreImageAsync(archive, image, cancellationToken).ConfigureAwait(false);
                if (stored is null)
                {
                    continue;
                }

                // Captioned from the paragraph the image sits in, falling back to the previous one.
                // Images here are frequently the actual answer, and a written question reaches them
                // only through this text.
                var caption = text.Length > 0 && !LooksLikeHeading(text) ? text : lastText;

                chunks.Add(new ExtractedChunk(
                    $"img{imageIndex++}",
                    section,
                    caption.Length > 0 ? caption : section,
                    stored));
            }
        }

        return chunks;
    }

    /// <summary>Copies one embedded image into the relay and returns the URL it will be served from.</summary>
    private async Task<string?> StoreImageAsync(
        ZipArchive archive,
        HtmlNode image,
        CancellationToken cancellationToken)
    {
        var source = image.GetAttributeValue("src", string.Empty);
        if (string.IsNullOrWhiteSpace(source))
        {
            return null;
        }

        // Entry names in the archive are exactly the relative src values.
        var entry = archive.Entries.FirstOrDefault(candidate =>
            candidate.FullName.Equals(source, StringComparison.OrdinalIgnoreCase));

        if (entry is null)
        {
            return null;
        }

        using var buffer = new MemoryStream();
        await using (var content = entry.Open())
        {
            await content.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        }

        var stored = await imageRelay
            .StoreAsync(buffer.ToArray(), contentType: null, cancellationToken)
            .ConfigureAwait(false);

        return stored.IsSuccess ? stored.Value : null;
    }

    private static List<ExtractedChunk> ParseText(string text, SourceDescriptor source)
    {
        var paragraphs = text
            .ReplaceLineEndings("\n")
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var chunks = new List<ExtractedChunk>();
        var section = DefaultSection(source);
        var index = 0;

        foreach (var paragraph in paragraphs)
        {
            if (LooksLikeHeading(paragraph))
            {
                // Tracked so each chunk carries where in the document it came from; that path is
                // shown in citations and folded into the embedding text.
                section = $"{DefaultSection(source)} > {paragraph}";
                continue;
            }

            // Positional, because neither export offers a stable per-paragraph id. Edits therefore
            // shift keys, which the ingest-run sweep cleans up rather than duplicating.
            chunks.Add(new ExtractedChunk($"p{index++}", section, paragraph));
        }

        return chunks;
    }

    private static string DefaultSection(SourceDescriptor source) =>
        source.Title ?? source.Country ?? "Document";

    /// <summary>
    /// Heuristic: the exports lose heading markup, so a heading is recognised by shape — short,
    /// standing alone, and not punctuated like a sentence. Wrong guesses only affect the section
    /// label, never whether content is indexed.
    /// </summary>
    private static bool LooksLikeHeading(string paragraph) =>
        paragraph.Length <= MaxHeadingLength
        && !paragraph.Contains('\n')
        && !paragraph.EndsWith('.')
        && !paragraph.EndsWith(',')
        && !paragraph.EndsWith(':');

    [LoggerMessage(LogLevel.Warning, "Could not fetch the Google document {DocumentId}.")]
    static partial void LogFetchFailed(ILogger logger, string documentId, Exception exception);

    [LoggerMessage(LogLevel.Warning, "Could not read the export archive for {DocumentId}.")]
    static partial void LogArchiveUnreadable(ILogger logger, string documentId, Exception exception);
}
