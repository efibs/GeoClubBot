using Microsoft.Extensions.Logging;
using UseCases.OutputPorts.AI.Ingestion;
using UseCases.UseCases.AI.Ingestion;
using Utilities;

namespace Infrastructure.OutputAdapters.AI.Extractors;

/// <summary>
/// Reads publicly shared Google Docs, which make up the largest share of the guide library.
///
/// Uses the plain-text export rather than the HTML one. For the same document the text export is
/// roughly 1.9 KB against 1.75 MB of HTML, because HTML inlines every image as base64 — a thousandfold
/// difference in transfer for content that is then thrown away by the parser anyway.
///
/// Images are therefore not indexed from Docs: the export embeds them rather than linking them, so
/// they have no URL the embedding provider could fetch. Serving them would need our own image relay.
/// </summary>
public sealed partial class GoogleDocSourceExtractor(
    IHttpClientFactory httpClientFactory,
    ILogger<GoogleDocSourceExtractor> logger) : ISourceExtractor
{
    /// <summary>A line this short, standing alone between blanks, is treated as a heading.</summary>
    private const int MaxHeadingLength = 80;

    public string SourceType => SourceLinkClassifier.GoogleDoc;

    public bool CanHandle(Uri url) =>
        SourceLinkClassifier.Classify(url).SourceType == SourceLinkClassifier.GoogleDoc;

    public async Task<Result<ExtractedDocument>> ExtractAsync(
        SourceDescriptor source,
        CancellationToken cancellationToken = default)
    {
        var endpoint = new Uri($"https://docs.google.com/document/d/{source.NaturalKey}/export?format=txt");

        string text;
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

            text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            LogFetchFailed(logger, source.NaturalKey, ex);
            return Error.Unexpected("ai.source_unreachable", $"Could not fetch the document {source.NaturalKey}.");
        }

        var chunks = Parse(text, source);

        return chunks.Count == 0
            ? Error.Validation("ai.document_empty", "This document has no readable text.")
            : new ExtractedDocument(source.Title, SourceUpdatedAtUtc: null, chunks);
    }

    private static List<ExtractedChunk> Parse(string text, SourceDescriptor source)
    {
        var paragraphs = text
            .ReplaceLineEndings("\n")
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var chunks = new List<ExtractedChunk>();
        var section = source.Title ?? source.Country ?? "Document";
        var index = 0;

        foreach (var paragraph in paragraphs)
        {
            if (LooksLikeHeading(paragraph))
            {
                // Tracked so each chunk carries where in the document it came from; that path is
                // shown in citations and folded into the embedding text.
                section = $"{source.Title ?? source.Country ?? "Document"} > {paragraph}";
                continue;
            }

            chunks.Add(new ExtractedChunk(
                // Positional, because the plain-text export offers no stable per-paragraph id. Edits
                // therefore shift keys, which the ingest-run sweep cleans up rather than duplicating.
                LocalKey: $"p{index}",
                SectionPath: section,
                Text: paragraph));

            index++;
        }

        return chunks;
    }

    /// <summary>
    /// Heuristic: the text export loses heading markup, so a heading is recognised by shape — short,
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
}
