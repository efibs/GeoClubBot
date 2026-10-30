using System.Text;
using Configuration;
using DocumentFormat.OpenXml.Packaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.OutputPorts.AI;
using UseCases.OutputPorts.AI.Ingestion;
using UseCases.UseCases.AI.Ingestion;
using Utilities;
using Drawing = DocumentFormat.OpenXml.Drawing;
using Presentation = DocumentFormat.OpenXml.Presentation;

namespace Infrastructure.OutputAdapters.AI.Extractors;

/// <summary>
/// Reads publicly shared Google Slides decks, which the library lists as "slideshow" guides.
///
/// Only the PowerPoint export carries the deck's content in full — the SVG export is not available —
/// and it is heavy: a median of 19 MB across the library's 41 indexed decks, and 339 MB for the largest,
/// against 2 KB for a document's text. That cost is why decks are indexed on the same paced nightly
/// schedule as everything else rather than on demand.
///
/// Slide images are embedded in the export with no URL of their own, so they are copied into the
/// image relay and served from this bot. Without a relay configured, decks are indexed text-only, from
/// the text export — and so is a deck too large for Google to export or for us to hold. That text has
/// no slide boundaries, so its chunks carry positional keys and no speaker notes or slide links. A deck
/// whose export times out or fails on Google's side is read from its text too, but marked degraded:
/// the pictures are missing this time only.
/// </summary>
public sealed partial class GoogleSlidesSourceExtractor(
    IHttpClientFactory httpClientFactory,
    IImageRelay imageRelay,
    IOptions<AiIngestionConfiguration> ingestionConfiguration,
    ILogger<GoogleSlidesSourceExtractor> logger) : ISourceExtractor
{
    public string SourceType => SourceLinkClassifier.GoogleSlides;

    public bool CanHandle(Uri url) =>
        SourceLinkClassifier.Classify(url).SourceType == SourceLinkClassifier.GoogleSlides;

    public async Task<Result<ExtractedDocument>> ExtractAsync(
        SourceDescriptor source,
        CancellationToken cancellationToken = default)
    {
        var settings = ingestionConfiguration.Value;

        // The PowerPoint export is only worth its weight — a median of 19 MB per library deck, against a
        // few kilobytes of text — when the slide images can be served afterwards.
        var wantsImages = imageRelay.IsEnabled && settings.EmbedImages;

        // Set when the text stands in for a deck that failed for a reason that may not recur, so the
        // caller can keep the pictures it already holds rather than trade them for this text.
        Error? deckFailure = null;

        if (wantsImages)
        {
            var deck = await FetchAsync(source, "pptx", settings.MaxDeckExportBytes, settings, cancellationToken)
                .ConfigureAwait(false);

            if (deck.IsSuccess)
            {
                return await ParseDeckAsync(deck.Value, source, cancellationToken).ConfigureAwait(false);
            }

            if (deck.Error.Code == SourceExtractionErrorCodes.ExportTooLarge)
            {
                // Google refuses to build some decks at all: one guide answered 413 after 30 seconds on
                // every run, while its text export — which held exactly what a thumbs-down asked for —
                // arrived in under two.
                LogExportTooLarge(logger, source.NaturalKey);
            }
            else if (deck.Error.Code == SourceExtractionErrorCodes.Unreachable)
            {
                // Timed out, or failed on Google's side: the same reasoning as a document's archive. The
                // text export is a separate, far lighter build that usually still answers.
                LogDeckUnavailable(logger, source.NaturalKey, deck.Error.Message);
                deckFailure = deck.Error;
            }
            else
            {
                // Deleted, private or a refusing host: the text export would be refused the same way.
                return deck.Error;
            }
        }

        var text = await FetchAsync(source, "txt", settings.MaxDocumentExportBytes, settings, cancellationToken)
            .ConfigureAwait(false);

        if (text.IsFailure)
        {
            return text.Error;
        }

        await using (text.Value.ConfigureAwait(false))
        {
            // Positional keys and no slide anchors: the text export marks neither slides nor notes.
            var chunks = GoogleDocSourceExtractor.ParseTextExport(
                Encoding.UTF8.GetString(text.Value.GetBuffer(), 0, (int)text.Value.Length),
                source.Title ?? source.Country ?? "Slides");

            if (chunks.Count > 0)
            {
                return new ExtractedDocument(source.Title, SourceUpdatedAtUtc: null, chunks, Degraded: deckFailure is not null);
            }

            // Text with nothing in it only settles the matter when it is all the deck will ever give. In
            // place of a deck that failed this time, it says nothing: a deck of pictures has only slide
            // titles for text, and skipping it as empty would end it for good — pictures indexed included
            // — over one timeout. Reported as that failure instead, so the deck is retried with backoff.
            return deckFailure ?? Error.Validation("ai.document_empty", "This deck has no readable text.");
        }
    }

    private async Task<Result<ExtractedDocument>> ParseDeckAsync(
        MemoryStream deck,
        SourceDescriptor source,
        CancellationToken cancellationToken)
    {
        try
        {
            // Buffered because the OpenXml reader needs random access, which the response stream
            // does not provide.
            await using (deck.ConfigureAwait(false))
            {
                return await ParseAsync(deck, source, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is FileFormatException or InvalidOperationException or OpenXmlPackageException)
        {
            LogParseFailed(logger, source.NaturalKey, ex);
            return Error.Unexpected("ai.source_unparsable", $"Could not read the deck {source.NaturalKey}.");
        }
    }

    /// <summary>Downloads one export of the deck, refusing to hold more of it than <paramref name="maxBytes"/>.</summary>
    private async Task<Result<MemoryStream>> FetchAsync(
        SourceDescriptor source,
        string format,
        long maxBytes,
        AiIngestionConfiguration settings,
        CancellationToken cancellationToken)
    {
        var endpoint = new Uri($"https://docs.google.com/presentation/d/{source.NaturalKey}/export/{format}");

        // Bounds the download as well as the request: with the headers read first, the HttpClient's own
        // timeout no longer covers the body, which for a deck is where the time goes.
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bounded.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, settings.SourceOverallTimeoutSeconds)));

        try
        {
            var client = httpClientFactory.CreateClient(PlonkItSourceExtractor.HttpClientName);

            // Headers first, so a deck past the limit is abandoned while it downloads rather than after
            // the whole of it has been buffered.
            using var response = await client
                .GetAsync(endpoint, HttpCompletionOption.ResponseHeadersRead, bounded.Token)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return SourceFetch.FromStatus(response.StatusCode, "deck", ForbiddenMeans.NotPublic);
            }

            return await SourceFetch.ReadCappedAsync(response, maxBytes, "deck", bounded.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (SourceFetch.IsFetchFailure(ex, cancellationToken))
        {
            return SourceFetch.FromException(ex, "deck", ForbiddenMeans.NotPublic);
        }
    }

    private async Task<Result<ExtractedDocument>> ParseAsync(
        Stream deck,
        SourceDescriptor source,
        CancellationToken cancellationToken)
    {
        using var document = PresentationDocument.Open(deck, isEditable: false);

        var presentationPart = document.PresentationPart;
        if (presentationPart?.Presentation?.SlideIdList is not { } slideIds)
        {
            return Error.Validation("ai.document_empty", "This deck has no slides.");
        }

        var chunks = new List<ExtractedChunk>();
        var position = 1;

        // Copying slide images is only worth it if they can be served afterwards.
        var wantsImages = imageRelay.IsEnabled && ingestionConfiguration.Value.EmbedImages;

        // Walked through the slide id list rather than SlideParts, whose order is not the presentation
        // order — numbering slides wrongly would make every deep link point at the wrong slide.
        foreach (var slideId in slideIds.ChildElements.OfType<Presentation.SlideId>())
        {
            if (slideId.RelationshipId?.Value is not { } relationshipId
                || presentationPart.GetPartById(relationshipId) is not SlidePart slidePart)
            {
                continue;
            }

            // The slide's own id, so re-reading a reordered deck lands on the same points.
            var slideKey = slideId.Id?.Value.ToString() ?? $"s{position}";
            var sectionPath = $"{source.Title ?? source.Country ?? "Slides"} > Slide {position}";
            var anchor = $"slide=id.p{position}";

            var text = ReadSlideText(slidePart);
            if (text.Length > 0)
            {
                chunks.Add(new ExtractedChunk(slideKey, sectionPath, text, ImageUrl: null, Anchor: anchor));
            }

            if (wantsImages)
            {
                // In these decks the slide is often mostly a picture and the words around it are the
                // explanation, so each image is captioned with its own slide's text.
                var caption = text.Length > 0 ? text : sectionPath;
                var imageIndex = 0;

                foreach (var imagePart in slidePart.ImageParts)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var stored = await StoreImageAsync(imagePart, cancellationToken).ConfigureAwait(false);
                    if (stored is not null)
                    {
                        chunks.Add(new ExtractedChunk(
                            $"{slideKey}-img{imageIndex++}", sectionPath, caption, stored, anchor));
                    }
                }
            }

            position++;
        }

        return chunks.Count == 0
            ? Error.Validation("ai.document_empty", "This deck has no readable text.")
            : new ExtractedDocument(source.Title, SourceUpdatedAtUtc: null, chunks);
    }

    /// <summary>Copies one embedded slide image into the relay and returns the URL it is served from.</summary>
    private async Task<string?> StoreImageAsync(ImagePart imagePart, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await using (var content = imagePart.GetStream())
        {
            await content.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        }

        var stored = await imageRelay
            .StoreAsync(buffer.ToArray(), imagePart.ContentType, cancellationToken)
            .ConfigureAwait(false);

        return stored.IsSuccess ? stored.Value : null;
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

    [LoggerMessage(LogLevel.Information,
        "The PowerPoint export of Google Slides deck {DeckId} is too large to take; indexing its text without images.")]
    static partial void LogExportTooLarge(ILogger logger, string deckId);

    [LoggerMessage(LogLevel.Warning,
        "The PowerPoint export of Google Slides deck {DeckId} failed ({Reason}); indexing its text without images.")]
    static partial void LogDeckUnavailable(ILogger logger, string deckId, string reason);

    [LoggerMessage(LogLevel.Warning, "Could not parse the Google Slides deck {DeckId}.")]
    static partial void LogParseFailed(ILogger logger, string deckId, Exception exception);
}
