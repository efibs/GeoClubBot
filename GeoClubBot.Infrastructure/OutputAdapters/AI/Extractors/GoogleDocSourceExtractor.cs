using System.IO.Compression;
using Configuration;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.OutputPorts.AI;
using UseCases.OutputPorts.AI.Ingestion;
using UseCases.UseCases.AI.Conversations;
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
///
/// That gap also has no upper bound worth trusting. One library document exports 332 MB, and Google
/// refuses to build an export larger still, answering 413 after the best part of a minute. Either way
/// the text export is what gets indexed instead — a few kilobytes, and everything the model reads. The
/// same goes for an archive that times out or fails on Google's side, except that the result is marked
/// degraded: the pictures are missing this time only.
/// </summary>
public sealed partial class GoogleDocSourceExtractor(
    IHttpClientFactory httpClientFactory,
    IImageRelay imageRelay,
    IOptions<AiIngestionConfiguration> ingestionConfiguration,
    ILogger<GoogleDocSourceExtractor> logger) : ISourceExtractor
{
    /// <summary>A line this short, standing alone between blanks, is treated as a heading.</summary>
    private const int MaxHeadingLength = 80;

    /// <summary>
    /// Heading elements a document needs before they alone mark its sections. One or two are often a
    /// title styled as a heading above sections in bold, whose names only the shape of a line gives
    /// away; reading such a document by its styles would lose every section name in it.
    /// </summary>
    private const int MinStyledHeadings = 3;

    private static readonly string[] BlockElements = ["p", "h1", "h2", "h3", "h4", "h5", "h6", "li"];

    private static readonly HashSet<string> HeadingElements = new(StringComparer.OrdinalIgnoreCase) { "h1", "h2", "h3", "h4", "h5", "h6" };

    public string SourceType => SourceLinkClassifier.GoogleDoc;

    public bool CanHandle(Uri url) =>
        SourceLinkClassifier.Classify(url).SourceType == SourceLinkClassifier.GoogleDoc;

    public async Task<Result<ExtractedDocument>> ExtractAsync(
        SourceDescriptor source,
        CancellationToken cancellationToken = default)
    {
        var settings = ingestionConfiguration.Value;

        // Fetching the heavier export is only worth it if the images can actually be served
        // afterwards; without a relay their bytes have nowhere to live.
        var wantsImages = imageRelay.IsEnabled && settings.EmbedImages;

        // Set when the text stands in for an archive that failed for a reason that may not recur, so
        // the caller can keep a fuller copy it already holds rather than trade it for this one.
        Error? archiveFailure = null;

        if (wantsImages)
        {
            var archive = await FetchAsync(source, "zip", settings, cancellationToken).ConfigureAwait(false);

            if (archive.IsSuccess)
            {
                await using (archive.Value.ConfigureAwait(false))
                {
                    return Document(
                        await ReadArchiveAsync(archive.Value, source, cancellationToken).ConfigureAwait(false),
                        source);
                }
            }

            if (archive.Error.Code == SourceExtractionErrorCodes.ExportTooLarge)
            {
                // The text is most of the value and costs kilobytes, so an export too big to take is
                // worth falling back on rather than giving up: these are guides of a few pages whose
                // embedded images happen to be enormous. Measured at 332 MB for one of them, against
                // around 10 KB of text.
                LogExportTooLarge(logger, source.NaturalKey);
            }
            else if (archive.Error.Code == SourceExtractionErrorCodes.Unreachable)
            {
                // Timed out, or failed on Google's side. The text export is a separate and far lighter
                // build that usually still answers, and without it a document whose archive never
                // finishes in time would never be indexed at all.
                LogArchiveUnavailable(logger, source.NaturalKey, archive.Error.Message);
                archiveFailure = archive.Error;
            }
            else
            {
                // Deleted, private or a refusing host: the text export would be refused the same way.
                return archive.Error;
            }
        }

        var text = await FetchAsync(source, "txt", settings, cancellationToken).ConfigureAwait(false);
        if (text.IsFailure)
        {
            return text.Error;
        }

        await using (text.Value.ConfigureAwait(false))
        {
            return Document(ParseTextExport(ReadUtf8(text.Value), DefaultSection(source)), source, archiveFailure);
        }
    }

    /// <param name="archiveFailure">
    /// Why the archive could not be read, when the text stands in for it for a reason that may not recur.
    /// The document is then marked degraded — and text with nothing in it settles nothing: a document of
    /// pictures has no text to speak of, and skipping it as empty would end it for good, pictures indexed
    /// included, over one timeout. That failure is reported instead, so the document is retried with backoff.
    /// </param>
    private static Result<ExtractedDocument> Document(
        List<ExtractedChunk> chunks,
        SourceDescriptor source,
        Error? archiveFailure = null) =>
        chunks.Count == 0
            ? archiveFailure ?? Error.Validation("ai.document_empty", "This document has no readable text.")
            : new ExtractedDocument(source.Title, SourceUpdatedAtUtc: null, chunks, Degraded: archiveFailure is not null);

    /// <summary>
    /// Downloads one export, refusing to hold more of it than <see cref="AiIngestionConfiguration.MaxDocumentExportBytes"/>.
    /// </summary>
    private async Task<Result<MemoryStream>> FetchAsync(
        SourceDescriptor source,
        string format,
        AiIngestionConfiguration settings,
        CancellationToken cancellationToken)
    {
        var endpoint = new Uri($"https://docs.google.com/document/d/{source.NaturalKey}/export?format={format}");

        // Bounds the download as well as the request. The HttpClient's own timeout stops once the
        // headers are in, so reading the body — which is where this spends its time — has nothing else
        // holding it to a limit.
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bounded.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, settings.SourceOverallTimeoutSeconds)));

        try
        {
            var client = httpClientFactory.CreateClient(PlonkItSourceExtractor.HttpClientName);

            // Headers first, so an oversized export is abandoned while it downloads rather than after.
            using var response = await client
                .GetAsync(endpoint, HttpCompletionOption.ResponseHeadersRead, bounded.Token)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return SourceFetch.FromStatus(response.StatusCode, "document", ForbiddenMeans.NotPublic);
            }

            return await SourceFetch.ReadCappedAsync(response, settings.MaxDocumentExportBytes, "document", bounded.Token)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (SourceFetch.IsFetchFailure(ex, cancellationToken))
        {
            LogFetchFailed(logger, source.NaturalKey, ex);
            return SourceFetch.FromException(ex, "document", ForbiddenMeans.NotPublic);
        }
    }

    private static string ReadUtf8(MemoryStream body) =>
        System.Text.Encoding.UTF8.GetString(body.GetBuffer(), 0, (int)body.Length);

    /// <summary>
    /// Reads the zip export: the document's HTML plus its images as separate entries, so each image
    /// can be stored and captioned with the text it sits in.
    /// </summary>
    private async Task<List<ExtractedChunk>> ReadArchiveAsync(
        MemoryStream payload,
        SourceDescriptor source,
        CancellationToken cancellationToken)
    {
        try
        {
            using var archive = new ZipArchive(payload, ZipArchiveMode.Read, leaveOpen: true);

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

        // A document whose author used heading styles says where its sections are, and nothing else is
        // guessed to be one. Only a document without them is read by the shape of its lines.
        var styled = (blocks?.Count(block => HeadingElements.Contains(block.Name)) ?? 0) >= MinStyledHeadings;

        var chunks = new List<ExtractedChunk>();
        var section = DefaultSection(source);
        var textIndex = 0;
        var imageIndex = 0;

        // The last non-empty text, so an image in a paragraph of its own still gets a caption.
        var lastText = string.Empty;

        foreach (var block in blocks ?? Enumerable.Empty<HtmlNode>())
        {
            var text = HtmlText.Decode(block.InnerText ?? string.Empty).Trim();
            var images = block.SelectNodes(".//img") ?? Enumerable.Empty<HtmlNode>();

            var isHeading = text.Length > 0 && IsHeading(block, text, styled);

            if (text.Length > 0)
            {
                if (isHeading)
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
                var caption = text.Length > 0 && !isHeading ? text : lastText;

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
        // Decoded before it is matched: the attribute is HTML-encoded and the entry name it has to
        // equal is not, so an encoded character would silently drop the image.
        var source = HtmlText.Decode(image.GetAttributeValue("src", string.Empty));
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

    /// <summary>
    /// Splits a plain-text export into paragraph chunks under the headings it can recognise. Shared
    /// with the Slides extractor, whose text export is the same shape.
    /// </summary>
    /// <param name="defaultSection">The section path for text before the first heading, and the root of every other.</param>
    internal static List<ExtractedChunk> ParseTextExport(string text, string defaultSection)
    {
        var paragraphs = text
            .ReplaceLineEndings("\n")
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var chunks = new List<ExtractedChunk>();
        var section = defaultSection;
        var index = 0;

        foreach (var paragraph in paragraphs)
        {
            if (LooksLikeHeading(paragraph))
            {
                // Tracked so each chunk carries where in the document it came from; that path is
                // shown in citations and folded into the embedding text.
                section = $"{defaultSection} > {paragraph}";
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
    /// Whether a block of the archive's HTML is a heading: a heading element, unless it is a credit, a
    /// URL or has no words (<see cref="SectionLabel.IsNeverAHeading"/>) — never for its length or its
    /// case, since its author chose to make it one, and a heading lost files every chunk under it under
    /// the heading before; and in a document that does not mark its sections with heading elements
    /// (<paramref name="styled"/>), also a line shaped like one that is neither a list item nor nothing
    /// but a link. "Counties (33) ->" and "You can view the flags in more detail here" were links, and
    /// became the section of every chunk after them.
    /// </summary>
    private static bool IsHeading(HtmlNode block, string text, bool styled) =>
        HeadingElements.Contains(block.Name)
            ? !SectionLabel.IsNeverAHeading(text)
            : !styled
              && !string.Equals(block.Name, "li", StringComparison.OrdinalIgnoreCase)
              && LooksLikeHeading(text)
              && !IsOnlyALink(block, text);

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
    /// Heuristic: the text export loses heading markup, so a heading is recognised by shape — short,
    /// standing alone, and not punctuated like a sentence — and by not being one of the lines that are
    /// shaped like one but are not (<see cref="SectionLabel.IsNotAHeading"/>): credits, URLs, filler,
    /// captions, or a line ending in an arrow. Those headed the answers of the 2026-10-29 replay as
    /// "Image credit to PlonkIt", "@bagaboiebailey", "Research by Yaron", "________________". Wrong
    /// guesses only affect the section label, never whether content is indexed.
    /// </summary>
    private static bool LooksLikeHeading(string paragraph) =>
        paragraph.Length <= MaxHeadingLength
        && !paragraph.Contains('\n')
        && !paragraph.EndsWith('.')
        && !paragraph.EndsWith(',')
        && !paragraph.EndsWith(':')
        && !SectionLabel.EndsWithArrow(paragraph)
        && !SectionLabel.IsNotAHeading(paragraph);

    [LoggerMessage(LogLevel.Information,
        "The export of Google document {DocumentId} is too large to take; indexing its text without images.")]
    static partial void LogExportTooLarge(ILogger logger, string documentId);

    [LoggerMessage(LogLevel.Warning,
        "The archive export of Google document {DocumentId} failed ({Reason}); indexing its text without images.")]
    static partial void LogArchiveUnavailable(ILogger logger, string documentId, string reason);

    [LoggerMessage(LogLevel.Warning, "Could not fetch the Google document {DocumentId}.")]
    static partial void LogFetchFailed(ILogger logger, string documentId, Exception exception);

    [LoggerMessage(LogLevel.Warning, "Could not read the export archive for {DocumentId}.")]
    static partial void LogArchiveUnreadable(ILogger logger, string documentId, Exception exception);
}
