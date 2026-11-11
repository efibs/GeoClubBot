using System.IO.Compression;
using System.Text.RegularExpressions;
using Configuration;
using Infrastructure.OutputAdapters.Http;
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
/// Three ways to read one, tried in this order:
///
/// - <b>The mobile page</b> (<c>/mobilebasic</c>): the document as plain HTML, half a megabyte to two,
///   with its headings as heading elements and every picture as a link of its own that serves it at
///   the size asked for.
/// - <b>The archive export</b> (<c>format=zip</c>): the same HTML with the pictures as files, at their
///   original size. Read when the page cannot be.
/// - <b>The text export</b> (<c>format=txt</c>): a few kilobytes, no pictures and no heading markup.
///   Read when neither of the others can be.
///
/// The page comes first because of what the archive cost. Its pictures are the originals, so its size
/// has no bound worth trusting: "A Learnable Colombia" exports 218 MB for 171 pictures, another library
/// document 332 MB, and past <see cref="AiIngestionConfiguration.MaxDocumentExportBytes"/> the text was
/// indexed instead. Measured on production on 2026-11-09, that was 86 of the 185 indexed documents —
/// "A Unique USA" with its 1,044 pictures among them — and it is what members rated down: "I would have
/// liked an image", of a clue whose document had lost all of them. The same 171 pictures arrive from
/// the page as JPEGs of about 200 kB each, one request at a time.
///
/// The text export costs more than pictures. It knows headings only by the shape of a line, and where
/// two stood together ("Helpful plants", "Ocotillo (Fouquieria splendens)") it read them as a paragraph:
/// the ocotillo's range was filed without its name, and answered as the range of the cholla, and of the
/// tree yucca. The page says which lines are headings, and how they nest.
///
/// The HTML export is never used: it inlines every picture as base64, costing the archive's bandwidth
/// while making the pictures harder to pull out.
/// </summary>
public sealed partial class GoogleDocSourceExtractor(
    IHttpClientFactory httpClientFactory,
    IImageRelay imageRelay,
    IOptions<AiIngestionConfiguration> ingestionConfiguration,
    ILogger<GoogleDocSourceExtractor> logger) : ISourceExtractor
{
    /// <summary>
    /// Pictures read from one document at most. The largest in the library holds 1,044; past this a
    /// document is something else, and its pictures would take the night.
    /// </summary>
    private const int MaxPictures = 2_000;

    /// <summary>
    /// Pictures in a row that may fail before the rest are left for another night. One missing picture
    /// costs a picture; a run of them is Google or the network, and asking a thousand more times would
    /// only hold the run up.
    /// </summary>
    private const int MaxConsecutivePictureFailures = 5;

    /// <summary>The most one picture may weigh as it is downloaded. The relay holds its own, lower limit.</summary>
    private const int MaxPictureDownloadBytes = 16 * 1024 * 1024;

    public string SourceType => SourceLinkClassifier.GoogleDoc;

    public bool CanHandle(Uri url) =>
        SourceLinkClassifier.Classify(url).SourceType == SourceLinkClassifier.GoogleDoc;

    public async Task<Result<ExtractedDocument>> ExtractAsync(
        SourceDescriptor source,
        CancellationToken cancellationToken = default)
    {
        var settings = ingestionConfiguration.Value;

        // Pictures are only worth fetching if they can be served afterwards; without a relay their
        // bytes have nowhere to live.
        var wantsImages = imageRelay.IsEnabled && settings.EmbedImages;

        // Set when a fuller reading failed for a reason that may not recur, so the caller can keep a
        // fuller copy it already holds rather than trade it for this one.
        Error? fullerFailure = null;

        var page = await FetchAsync(source, PageEndpoint(source), settings, isPage: true, cancellationToken).ConfigureAwait(false);
        if (page.IsSuccess)
        {
            await using (page.Value.ConfigureAwait(false))
            {
                var pieces = GoogleDocHtml.Read(ReadUtf8(page.Value), DefaultSection(source));
                if (pieces.Count > 0)
                {
                    var downloads = new PictureDownloads(
                        httpClientFactory.CreateClient(PlonkItSourceExtractor.HttpClientName), settings.DocumentPicturePixels);

                    var read = await ToChunksAsync(
                            pieces,
                            wantsImages ? (picture, token) => DownloadPictureAsync(downloads, picture, source, token) : null,
                            source,
                            cancellationToken)
                        .ConfigureAwait(false);

                    return Document(read.Chunks, source, read.Complete ? null : PicturesIncomplete);
                }

                // Answered, but with something that is no document: the exports say what is wrong with it.
                LogPageUnreadable(logger, source.NaturalKey);
            }
        }
        else if (page.Error.Code == SourceExtractionErrorCodes.HostRefused)
        {
            // About Google, not this document: the exports would only be refused as well.
            return page.Error;
        }
        else if (page.Error.Code == SourceExtractionErrorCodes.Unreachable)
        {
            LogPageUnavailable(logger, source.NaturalKey, page.Error.Message);
            fullerFailure = page.Error;
        }

        if (wantsImages)
        {
            var archive = await FetchAsync(source, ExportEndpoint(source, "zip"), settings, isPage: false, cancellationToken)
                .ConfigureAwait(false);

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
                fullerFailure = archive.Error;
            }
            else
            {
                // Deleted, private or a refusing host: the text export would be refused the same way.
                return archive.Error;
            }
        }

        var text = await FetchAsync(source, ExportEndpoint(source, "txt"), settings, isPage: false, cancellationToken)
            .ConfigureAwait(false);
        if (text.IsFailure)
        {
            return text.Error;
        }

        await using (text.Value.ConfigureAwait(false))
        {
            return Document(ParseTextExport(ReadUtf8(text.Value), DefaultSection(source)), source, fullerFailure);
        }
    }

    /// <summary>What a document read without all of its pictures is marked with: they may arrive another night.</summary>
    private static readonly Error PicturesIncomplete = Error.Unexpected(
        SourceExtractionErrorCodes.Unreachable, "Some of this document's pictures could not be downloaded.");

    private static Uri PageEndpoint(SourceDescriptor source) =>
        new($"https://docs.google.com/document/d/{source.NaturalKey}/mobilebasic");

    private static Uri ExportEndpoint(SourceDescriptor source, string format) =>
        new($"https://docs.google.com/document/d/{source.NaturalKey}/export?format={format}");

    /// <param name="fullerFailure">
    /// Why a fuller reading could not be had, when what was read stands in for it for a reason that may
    /// not recur. The document is then marked degraded — and text with nothing in it settles nothing: a
    /// document of pictures has no text to speak of, and skipping it as empty would end it for good,
    /// pictures indexed included, over one timeout. That failure is reported instead, so the document is
    /// retried with backoff.
    /// </param>
    private static Result<ExtractedDocument> Document(
        List<ExtractedChunk> chunks,
        SourceDescriptor source,
        Error? fullerFailure = null) =>
        chunks.Count == 0
            ? fullerFailure ?? Error.Validation("ai.document_empty", "This document has no readable text.")
            : new ExtractedDocument(source.Title, SourceUpdatedAtUtc: null, chunks, Degraded: fullerFailure is not null);

    /// <summary>
    /// Downloads the page or one export, refusing to hold more of it than <see cref="AiIngestionConfiguration.MaxDocumentExportBytes"/>.
    /// </summary>
    /// <param name="isPage">
    /// The page is followed to wherever Google sends the request, and a document that is not shared
    /// sends it to a sign-in form, which answers 200. Only the page itself counts.
    /// </param>
    private async Task<Result<MemoryStream>> FetchAsync(
        SourceDescriptor source,
        Uri endpoint,
        AiIngestionConfiguration settings,
        bool isPage,
        CancellationToken cancellationToken)
    {
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

            if (isPage && response.RequestMessage?.RequestUri is { } answeredBy && !IsDocumentPage(answeredBy))
            {
                return Error.Forbidden(SourceExtractionErrorCodes.NotPublic,
                    "This document's page led somewhere else, as it does for one that is not shared.");
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

    private static bool IsDocumentPage(Uri url) =>
        url.Host.Equals("docs.google.com", StringComparison.OrdinalIgnoreCase)
        && url.AbsolutePath.Contains("/mobilebasic", StringComparison.OrdinalIgnoreCase);

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

            var read = await ToChunksAsync(
                    GoogleDocHtml.Read(html, DefaultSection(source)),
                    async (picture, token) => new PictureResult(await StoreEntryAsync(archive, picture, token).ConfigureAwait(false)),
                    source,
                    cancellationToken)
                .ConfigureAwait(false);

            return read.Chunks;
        }
        catch (InvalidDataException ex)
        {
            LogArchiveUnreadable(logger, source.NaturalKey, ex);
            return [];
        }
    }

    /// <summary>
    /// Turns a document's pieces into chunks, fetching each picture once. A piece keeps its place in the
    /// key whether or not its picture arrived, so a picture that is missing one night and back the next
    /// does not re-key every chunk after it.
    ///
    /// A picture that cannot be stored costs the picture, never the words: when all the pictures sharing
    /// a caption are lost and the caption holds paragraphs that are in no text chunk, it is kept as text.
    /// </summary>
    /// <param name="store">Fetches and stores one picture by its <c>src</c>; null when pictures are not wanted.</param>
    /// <returns>The chunks, and whether every picture was at least asked for.</returns>
    private async Task<(List<ExtractedChunk> Chunks, bool Complete)> ToChunksAsync(
        IReadOnlyList<GoogleDocPiece> pieces,
        Func<string, CancellationToken, Task<PictureResult>>? store,
        SourceDescriptor source,
        CancellationToken cancellationToken)
    {
        var stored = new string?[pieces.Count];
        var complete = true;

        if (store is not null)
        {
            var bySource = new Dictionary<string, string?>(StringComparer.Ordinal);
            var stopped = false;

            for (var index = 0; index < pieces.Count; index++)
            {
                if (pieces[index].ImageSource is not { } picture)
                {
                    continue;
                }

                if (!bySource.TryGetValue(picture, out var url))
                {
                    if (stopped)
                    {
                        continue;
                    }

                    if (bySource.Count >= MaxPictures)
                    {
                        LogTooManyPictures(logger, source.NaturalKey, MaxPictures);
                        stopped = true;
                        continue;
                    }

                    cancellationToken.ThrowIfCancellationRequested();

                    var result = await store(picture, cancellationToken).ConfigureAwait(false);
                    url = result.Url;
                    bySource[picture] = url;

                    if (result.Stop)
                    {
                        LogPicturesStopped(logger, source.NaturalKey, bySource.Count);
                        stopped = true;
                        complete = false;
                    }
                }

                stored[index] = url;
            }
        }

        var groupsWithPicture = new HashSet<int>();
        for (var index = 0; index < pieces.Count; index++)
        {
            if (stored[index] is not null && pieces[index].Group is { } group)
            {
                groupsWithPicture.Add(group);
            }
        }

        var chunks = new List<ExtractedChunk>();
        var groupsKeptAsText = new HashSet<int>();

        for (var index = 0; index < pieces.Count; index++)
        {
            var piece = pieces[index];
            var key = $"p{index.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

            if (piece.ImageSource is null)
            {
                chunks.Add(new ExtractedChunk(key, piece.SectionPath, piece.Text, Anchor: piece.Anchor));
            }
            else if (stored[index] is { } url)
            {
                // A picture nothing near it describes is captioned by where it stands.
                chunks.Add(new ExtractedChunk(
                    $"{key}-img", piece.SectionPath, piece.Text.Length > 0 ? piece.Text : piece.SectionPath, url, piece.Anchor));
            }
            else if (piece is { OwnsText: true, Text.Length: > 0, Group: { } group }
                     && !groupsWithPicture.Contains(group)
                     && groupsKeptAsText.Add(group))
            {
                chunks.Add(new ExtractedChunk(key, piece.SectionPath, piece.Text, Anchor: piece.Anchor));
            }
        }

        return (chunks, complete);
    }

    /// <summary>
    /// Downloads one of the page's pictures and stores it in the relay. The page links each picture at
    /// 800 pixels; the size is part of the link, and <see cref="AiIngestionConfiguration.DocumentPicturePixels"/>
    /// is asked for instead, so a map's small print stays readable.
    ///
    /// Only Google's own hosts are asked: the page is Google's, and nothing in it should send the bot
    /// anywhere else.
    /// </summary>
    private async Task<PictureResult> DownloadPictureAsync(
        PictureDownloads downloads,
        string picture,
        SourceDescriptor source,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(picture, UriKind.Absolute, out var url)
            || url.Scheme != Uri.UriSchemeHttps
            || !IsGoogleHost(url.Host))
        {
            return new PictureResult(null);
        }

        var sized = new Uri(PictureSize().Replace(
            url.AbsoluteUri, $"=s{downloads.Pixels.ToString(System.Globalization.CultureInfo.InvariantCulture)}", 1));

        try
        {
            using var response = await downloads.Client
                .GetAsync(sized, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                LogPictureFailed(logger, source.NaturalKey, (int)response.StatusCode);

                // Google turning requests away is about the night, not the picture.
                return new PictureResult(null, Stop: (int)response.StatusCode == 429 || downloads.Failed());
            }

            var body = await SourceFetch.ReadCappedAsync(response, MaxPictureDownloadBytes, "picture", cancellationToken)
                .ConfigureAwait(false);
            if (body.IsFailure)
            {
                return new PictureResult(null, Stop: downloads.Failed());
            }

            await using (body.Value.ConfigureAwait(false))
            {
                var saved = await imageRelay
                    .StoreAsync(body.Value.ToArray(), response.Content.Headers.ContentType?.MediaType, cancellationToken)
                    .ConfigureAwait(false);

                downloads.Succeeded();
                return new PictureResult(saved.IsSuccess ? saved.Value : null);
            }
        }
        catch (HostRefusedRequestException)
        {
            return new PictureResult(null, Stop: true);
        }
        catch (Exception ex) when (SourceFetch.IsFetchFailure(ex, cancellationToken))
        {
            LogPictureThrew(logger, source.NaturalKey, ex);
            return new PictureResult(null, Stop: downloads.Failed());
        }
    }

    private static bool IsGoogleHost(string host) =>
        host.Equals("google.com", StringComparison.OrdinalIgnoreCase)
        || host.EndsWith(".google.com", StringComparison.OrdinalIgnoreCase)
        || host.Equals("googleusercontent.com", StringComparison.OrdinalIgnoreCase)
        || host.EndsWith(".googleusercontent.com", StringComparison.OrdinalIgnoreCase);

    /// <summary>Copies one of the archive's pictures into the relay and returns the URL it will be served from.</summary>
    private async Task<string?> StoreEntryAsync(ZipArchive archive, string picture, CancellationToken cancellationToken)
    {
        // Entry names in the archive are exactly the relative src values.
        var entry = archive.Entries.FirstOrDefault(candidate =>
            candidate.FullName.Equals(picture, StringComparison.OrdinalIgnoreCase));

        if (entry is null)
        {
            return null;
        }

        using var buffer = new MemoryStream();
        await using (var content = entry.Open())
        {
            await content.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        }

        var saved = await imageRelay
            .StoreAsync(buffer.ToArray(), contentType: null, cancellationToken)
            .ConfigureAwait(false);

        return saved.IsSuccess ? saved.Value : null;
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
            // The text export loses heading markup, so a heading is recognised by its shape. Wrong
            // guesses only affect the section label, never whether content is indexed.
            if (GoogleDocHtml.LooksLikeHeading(paragraph))
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

    /// <param name="Url">Where the stored picture is served from, or null when it could not be stored.</param>
    /// <param name="Stop">Ask for no more of this document's pictures this run.</param>
    private readonly record struct PictureResult(string? Url, bool Stop = false);

    /// <summary>One document's picture downloads: the client they share, and how many failed in a row.</summary>
    private sealed class PictureDownloads(HttpClient client, int pixels)
    {
        private int _consecutiveFailures;

        public HttpClient Client { get; } = client;

        public int Pixels { get; } = Math.Clamp(pixels, 200, 4_000);

        public void Succeeded() => _consecutiveFailures = 0;

        /// <summary>Counts a failure, and says whether it is one too many in a row.</summary>
        public bool Failed() => ++_consecutiveFailures >= MaxConsecutivePictureFailures;
    }

    // The size a picture link asks for: "=s800" closing the path, before the link's key.
    [GeneratedRegex(@"=s[0-9]+(?=$|\?)", RegexOptions.CultureInvariant)]
    private static partial Regex PictureSize();

    [LoggerMessage(LogLevel.Information,
        "The page of Google document {DocumentId} holds no document; reading its exports instead.")]
    static partial void LogPageUnreadable(ILogger logger, string documentId);

    [LoggerMessage(LogLevel.Warning,
        "The page of Google document {DocumentId} could not be read ({Reason}); reading its exports instead.")]
    static partial void LogPageUnavailable(ILogger logger, string documentId, string reason);

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

    [LoggerMessage(LogLevel.Debug, "A picture of Google document {DocumentId} answered HTTP {Status}; indexing the document without it.")]
    static partial void LogPictureFailed(ILogger logger, string documentId, int status);

    [LoggerMessage(LogLevel.Debug, "A picture of Google document {DocumentId} could not be downloaded; indexing the document without it.")]
    static partial void LogPictureThrew(ILogger logger, string documentId, Exception exception);

    [LoggerMessage(LogLevel.Warning,
        "Stopped downloading the pictures of Google document {DocumentId} after {Asked}: too many failed in a row. The rest are left for a later run.")]
    static partial void LogPicturesStopped(ILogger logger, string documentId, int asked);

    [LoggerMessage(LogLevel.Warning,
        "Google document {DocumentId} holds more than {MaxPictures} pictures; the rest are not indexed.")]
    static partial void LogTooManyPictures(ILogger logger, string documentId, int maxPictures);
}
