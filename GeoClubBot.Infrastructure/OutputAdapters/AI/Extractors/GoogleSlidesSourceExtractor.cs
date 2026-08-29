using System.Text;
using DocumentFormat.OpenXml.Packaging;
using Microsoft.Extensions.Logging;
using UseCases.OutputPorts.AI.Ingestion;
using UseCases.UseCases.AI.Ingestion;
using Utilities;
using Drawing = DocumentFormat.OpenXml.Drawing;
using Presentation = DocumentFormat.OpenXml.Presentation;

namespace Infrastructure.OutputAdapters.AI.Extractors;

/// <summary>
/// Reads publicly shared Google Slides decks, which the library lists as "slideshow" guides.
///
/// Only the PowerPoint export carries the deck's content — the SVG export is not available — and it is
/// heavy, around 7 MB per deck against 2 KB for a document's text. That cost is why decks are indexed
/// on the same paced nightly schedule as everything else rather than on demand.
///
/// Slide images are not indexed: the export embeds them with no public URL for the embedding provider
/// to fetch, so reaching them would need our own image relay.
/// </summary>
public sealed partial class GoogleSlidesSourceExtractor(
    IHttpClientFactory httpClientFactory,
    ILogger<GoogleSlidesSourceExtractor> logger) : ISourceExtractor
{
    public string SourceType => SourceLinkClassifier.GoogleSlides;

    public bool CanHandle(Uri url) =>
        SourceLinkClassifier.Classify(url).SourceType == SourceLinkClassifier.GoogleSlides;

    public async Task<Result<ExtractedDocument>> ExtractAsync(
        SourceDescriptor source,
        CancellationToken cancellationToken = default)
    {
        var endpoint = new Uri($"https://docs.google.com/presentation/d/{source.NaturalKey}/export/pptx");

        Stream deck;
        try
        {
            var client = httpClientFactory.CreateClient(PlonkItSourceExtractor.HttpClientName);
            using var response = await client.GetAsync(endpoint, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return (int)response.StatusCode is 401 or 403 or 404
                    ? Error.Validation("ai.document_not_public",
                        $"This deck is not publicly readable (HTTP {(int)response.StatusCode}).")
                    : Error.Unexpected("ai.source_unreachable",
                        $"Could not fetch the deck (HTTP {(int)response.StatusCode}).");
            }

            // Buffered because the OpenXml reader needs random access, which the response stream
            // does not provide.
            deck = new MemoryStream(await response.Content.ReadAsByteArrayAsync(cancellationToken)
                .ConfigureAwait(false));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return Error.Unexpected("ai.source_unreachable", $"Could not fetch the deck {source.NaturalKey}.");
        }

        try
        {
            await using (deck.ConfigureAwait(false))
            {
                return Parse(deck, source);
            }
        }
        catch (Exception ex) when (ex is FileFormatException or InvalidOperationException or OpenXmlPackageException)
        {
            LogParseFailed(logger, source.NaturalKey, ex);
            return Error.Unexpected("ai.source_unparsable", $"Could not read the deck {source.NaturalKey}.");
        }
    }

    private static Result<ExtractedDocument> Parse(Stream deck, SourceDescriptor source)
    {
        using var document = PresentationDocument.Open(deck, isEditable: false);

        var presentationPart = document.PresentationPart;
        if (presentationPart?.Presentation?.SlideIdList is not { } slideIds)
        {
            return Error.Validation("ai.document_empty", "This deck has no slides.");
        }

        var chunks = new List<ExtractedChunk>();
        var position = 1;

        // Walked through the slide id list rather than SlideParts, whose order is not the presentation
        // order — numbering slides wrongly would make every deep link point at the wrong slide.
        foreach (var slideId in slideIds.ChildElements.OfType<Presentation.SlideId>())
        {
            if (slideId.RelationshipId?.Value is not { } relationshipId
                || presentationPart.GetPartById(relationshipId) is not SlidePart slidePart)
            {
                continue;
            }

            var text = ReadSlideText(slidePart);
            if (text.Length > 0)
            {
                chunks.Add(new ExtractedChunk(
                    // The slide's own id, so re-reading a reordered deck lands on the same points.
                    LocalKey: slideId.Id?.Value.ToString() ?? $"s{position}",
                    SectionPath: $"{source.Title ?? source.Country ?? "Slides"} > Slide {position}",
                    Text: text,
                    ImageUrl: null,
                    Anchor: $"slide=id.p{position}"));
            }

            position++;
        }

        return chunks.Count == 0
            ? Error.Validation("ai.document_empty", "This deck has no readable text.")
            : new ExtractedDocument(source.Title, SourceUpdatedAtUtc: null, chunks);
    }

    /// <summary>
    /// Slide body text plus its speaker notes, which in these guides often carry the actual
    /// explanation while the slide itself is mostly a picture.
    /// </summary>
    private static string ReadSlideText(SlidePart slidePart)
    {
        var builder = new StringBuilder();

        if (slidePart.Slide is null)
        {
            return string.Empty;
        }

        foreach (var paragraph in slidePart.Slide.Descendants<Drawing.Paragraph>())
        {
            var line = string.Concat(paragraph.Descendants<Drawing.Text>().Select(text => text.Text));
            if (!string.IsNullOrWhiteSpace(line))
            {
                builder.AppendLine(line.Trim());
            }
        }

        var notes = slidePart.NotesSlidePart?.NotesSlide?.Descendants<Drawing.Text>();
        if (notes is not null)
        {
            var noteText = string.Concat(notes.Select(text => text.Text)).Trim();
            if (noteText.Length > 0)
            {
                builder.AppendLine().Append("Notes: ").Append(noteText);
            }
        }

        return builder.ToString().Trim();
    }

    [LoggerMessage(LogLevel.Warning, "Could not parse the Google Slides deck {DeckId}.")]
    static partial void LogParseFailed(ILogger logger, string deckId, Exception exception);
}
