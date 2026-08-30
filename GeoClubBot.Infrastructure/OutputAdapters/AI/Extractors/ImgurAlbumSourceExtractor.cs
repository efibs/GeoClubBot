using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using UseCases.OutputPorts.AI.Ingestion;
using UseCases.UseCases.AI.Ingestion;
using Utilities;

namespace Infrastructure.OutputAdapters.AI.Extractors;

/// <summary>
/// Reads imgur albums, which in this library are overwhelmingly infographics — often the single most
/// useful artefact for a given meta, since a picture of a bollard beats a paragraph describing one.
///
/// Uses imgur's own album endpoint rather than scraping the album page: the page is rendered
/// client-side and yields nothing, while this endpoint needs no API key and returns each image's id,
/// extension, title and description directly.
/// </summary>
public sealed partial class ImgurAlbumSourceExtractor(
    IHttpClientFactory httpClientFactory,
    ILogger<ImgurAlbumSourceExtractor> logger) : ISourceExtractor
{
    public string SourceType => SourceLinkClassifier.ImgurAlbum;

    public bool CanHandle(Uri url) =>
        SourceLinkClassifier.Classify(url).SourceType == SourceLinkClassifier.ImgurAlbum;

    public async Task<Result<ExtractedDocument>> ExtractAsync(
        SourceDescriptor source,
        CancellationToken cancellationToken = default)
    {
        var endpoint = new Uri($"https://imgur.com/ajaxalbums/getimages/{source.NaturalKey}/hit.json");

        string payload;
        try
        {
            var client = httpClientFactory.CreateClient(PlonkItSourceExtractor.HttpClientName);
            payload = await client.GetStringAsync(endpoint, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return Error.Unexpected("ai.source_unreachable", $"Could not fetch the album {source.NaturalKey}.");
        }

        try
        {
            return Parse(payload, source);
        }
        catch (JsonException ex)
        {
            LogParseFailed(logger, source.NaturalKey, ex);
            return Error.Unexpected("ai.source_unparsable", $"Could not read the album {source.NaturalKey}.");
        }
    }

    private static Result<ExtractedDocument> Parse(string payload, SourceDescriptor source)
    {
        using var document = JsonDocument.Parse(payload);

        if (!document.RootElement.TryGetProperty("data", out var data)
            || !data.TryGetProperty("images", out var images)
            || images.ValueKind != JsonValueKind.Array)
        {
            // A deleted or private album answers with no image list. Reported as validation so the
            // caller records it as skipped rather than retrying it nightly forever.
            return Error.Validation("ai.album_unavailable", "This imgur album has no readable images.");
        }

        var chunks = new List<ExtractedChunk>();
        var index = 0;

        foreach (var image in images.EnumerateArray())
        {
            var hash = image.TryGetProperty("hash", out var hashElement) ? hashElement.GetString() : null;
            if (string.IsNullOrWhiteSpace(hash))
            {
                continue;
            }

            var extension = image.TryGetProperty("ext", out var extElement) ? extElement.GetString() : ".png";

            chunks.Add(new ExtractedChunk(
                // The image's own hash, so re-reading the album lands on the same points even if its
                // ordering changes.
                LocalKey: hash,
                SectionPath: source.Title ?? source.NaturalKey,
                Text: BuildCaption(image, source),
                ImageUrl: $"https://i.imgur.com/{hash}{extension}"));

            index++;
        }

        return chunks.Count == 0
            ? Error.Validation("ai.album_unavailable", "This imgur album contains no images.")
            : new ExtractedDocument(source.Title, SourceUpdatedAtUtc: null, chunks);
    }

    /// <summary>
    /// An infographic's caption is how a written question finds it, so everything the library and the
    /// album know about the image is folded in — the album's own title and description are often
    /// empty, and the catalogue entry is then the only text there is.
    /// </summary>
    private static string BuildCaption(JsonElement image, SourceDescriptor source)
    {
        var parts = new List<string?>
        {
            source.Country,
            source.Title,
            image.TryGetProperty("title", out var title) ? title.GetString() : null,
            image.TryGetProperty("description", out var description) ? description.GetString() : null
        };

        var caption = new StringBuilder();
        foreach (var part in parts.Where(part => !string.IsNullOrWhiteSpace(part)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (caption.Length > 0)
            {
                caption.Append(" — ");
            }

            caption.Append(part!.Trim());
        }

        return caption.ToString();
    }

    [LoggerMessage(LogLevel.Warning, "Could not parse the imgur album {AlbumId}.")]
    static partial void LogParseFailed(ILogger logger, string albumId, Exception exception);
}
