using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SkiaSharp;
using UseCases.OutputPorts.AI;
using Utilities;

namespace Infrastructure.OutputAdapters.AI.PlantNet;

/// <summary>
/// Names a picture's plant with Pl@ntNet's identification API (<c>POST /v2/identify/{project}</c>,
/// the picture as a multipart file, the key as a query parameter). Free up to 500 identifications a
/// day per account, reset at 00:00 UTC; past that the service answers 429, and this adapter stops
/// one step earlier at its own cap so the account keeps room.
///
/// The pictures are downloaded from their links (Discord attachments) and forwarded, never linked: the
/// API takes files only, up to five views of one plant per identification. JPEG and PNG go as they are;
/// anything else SkiaSharp can read (WebP, GIF, BMP) is sent as a JPEG. Pl@ntNet keeps the pictures in
/// memory for the identification and stores only its request log (date, time), per its terms. Answers
/// are cached by the pictures' links, without their signatures, so a follow-up on the same screenshot
/// costs no identification.
///
/// It asks for <see cref="AiPlantIdentificationConfiguration.CandidatesConsidered"/> candidates, not the
/// few offered to the model, because where the screenshot was taken re-orders them; and for the genera
/// (<c>detailed=true</c>), which on a Street View frame are often sure when the species is not.
/// </summary>
public sealed partial class PlantNetPlantIdentifier(
    IHttpClientFactory httpClientFactory,
    IOptions<AiToolsConfiguration> configuration,
    TimeProvider timeProvider,
    ILogger<PlantNetPlantIdentifier> logger) : IPlantIdentifier
{
    public const string HttpClientName = "PlantNet";

    /// <summary>Cached answers at most; past it the cache starts again rather than grow without bound.</summary>
    private const int MaxCachedAnswers = 256;

    /// <summary>What Pl@ntNet takes: JPEG and PNG.</summary>
    private static readonly HashSet<string> AcceptedContentTypes =
    [
        with(StringComparer.OrdinalIgnoreCase),
        "image/jpeg", "image/jpg", "image/png"
    ];

    /// <summary>What is converted to JPEG before it is sent: what Discord serves besides JPEG and PNG.</summary>
    private static readonly HashSet<string> ConvertedContentTypes =
    [
        with(StringComparer.OrdinalIgnoreCase),
        "image/webp", "image/gif", "image/bmp"
    ];

    /// <summary>The quality a converted picture is sent at: what Pl@ntNet recommends.</summary>
    private const int JpegQuality = 90;

    /// <summary>Pictures Pl@ntNet takes per identification.</summary>
    private const int MaxPictures = 5;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly ConcurrentDictionary<string, CachedAnswer> _cache = new(StringComparer.Ordinal);
    private readonly Lock _quotaLock = new();
    private DateOnly _countedDay;
    private int _requestsToday;
    private bool _serviceExhaustedToday;

    public bool IsAvailable
    {
        get
        {
            var settings = configuration.Value.PlantIdentification;
            return settings.Enabled && !string.IsNullOrWhiteSpace(settings.ApiKey);
        }
    }

    public async Task<Result<PlantIdentification>> IdentifyAsync(IReadOnlyList<string> imageUrls, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(imageUrls);

        var pictures = imageUrls.Where(url => !string.IsNullOrWhiteSpace(url)).Distinct(StringComparer.Ordinal).Take(MaxPictures).ToList();
        if (pictures.Count == 0)
        {
            throw new ArgumentException("At least one picture is needed.", nameof(imageUrls));
        }

        var settings = configuration.Value.PlantIdentification;
        if (!IsAvailable)
        {
            return Error.Validation(PlantIdentificationErrorCodes.Unavailable, "Plant identification is turned off or has no API key.");
        }

        var now = timeProvider.GetUtcNow();
        var key = string.Join('|', pictures.Select(CacheKeyOf));

        if (_cache.TryGetValue(key, out var cached) && cached.ExpiresAt > now)
        {
            return cached.Answer;
        }

        if (!TryClaimRequest(DateOnly.FromDateTime(now.UtcDateTime), settings.MaxRequestsPerDay))
        {
            LogQuotaSpent(logger, settings.MaxRequestsPerDay);
            return Error.Conflict(PlantIdentificationErrorCodes.QuotaExceeded, "Today's plant identifications are spent.");
        }

        var answer = await IdentifyUncachedAsync(pictures, settings, cancellationToken).ConfigureAwait(false);

        // A failure to reach the service is not an answer about the picture, and is asked again next time.
        if ((answer.IsSuccess || answer.Error.Type == ErrorType.NotFound) && settings.CacheHours > 0)
        {
            if (_cache.Count >= MaxCachedAnswers)
            {
                _cache.Clear();
            }

            _cache[key] = new CachedAnswer(answer, now + TimeSpan.FromHours(settings.CacheHours));
        }

        return answer;
    }

    private async Task<Result<PlantIdentification>> IdentifyUncachedAsync(
        IReadOnlyList<string> imageUrls,
        AiPlantIdentificationConfiguration settings,
        CancellationToken cancellationToken)
    {
        try
        {
            var client = httpClientFactory.CreateClient(HttpClientName);

            // A picture that cannot be sent is left out; the others still show the plant.
            var pictures = new List<Picture>();
            Error? unusable = null;
            foreach (var imageUrl in imageUrls)
            {
                var picture = await DownloadAsync(client, imageUrl, settings, cancellationToken).ConfigureAwait(false);
                if (picture.IsSuccess)
                {
                    pictures.Add(picture.Value);
                }
                else
                {
                    unusable ??= picture.Error;
                }
            }

            if (pictures.Count == 0)
            {
                return unusable ?? Error.Validation(PlantIdentificationErrorCodes.ImageUnusable, "No picture could be sent.");
            }

            var candidates = Math.Max(settings.MaxCandidates, settings.CandidatesConsidered);
            var url = $"{settings.BaseUrl.TrimEnd('/')}/v2/identify/{Uri.EscapeDataString(settings.Project)}"
                      + $"?api-key={Uri.EscapeDataString(settings.ApiKey)}"
                      + $"&lang={Uri.EscapeDataString(settings.Language)}"
                      + $"&nb-results={Math.Max(1, candidates).ToString(CultureInfo.InvariantCulture)}"
                      + "&include-related-images=false"
                      + "&detailed=true";

            using var form = new MultipartFormDataContent();
            for (var index = 0; index < pictures.Count; index++)
            {
                // Names quoted by hand: .NET writes `name=images; filename=x.png` bare, which the stricter
                // multipart parsers read as no file at all.
                var picture = pictures[index];
                var fileName = $"screenshot{(index == 0 ? string.Empty : (index + 1).ToString(CultureInfo.InvariantCulture))}"
                               + (picture.ContentType == "image/png" ? ".png" : ".jpg");
                var file = new ByteArrayContent(picture.Bytes);
                file.Headers.ContentType = new MediaTypeHeaderValue(picture.ContentType);
                file.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data")
                {
                    Name = "\"images\"",
                    FileName = $"\"{fileName}\""
                };
                form.Add(file);

                // One organ per picture, in the same order; "auto" lets Pl@ntNet tell a leaf from a whole tree.
                var organs = new StringContent("auto");
                organs.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data") { Name = "\"organs\"" };
                form.Add(organs);
            }

            using var response = await client.PostAsync(url, form, cancellationToken).ConfigureAwait(false);

            switch (response.StatusCode)
            {
                case HttpStatusCode.NotFound:
                    // "Species not found": nothing it knows is in the picture.
                    LogNotRecognised(logger);
                    return Error.NotFound(PlantIdentificationErrorCodes.NotRecognised, "Pl@ntNet recognises no plant in the picture.");

                case HttpStatusCode.TooManyRequests:
                    MarkServiceExhausted();
                    LogServiceQuota(logger);
                    return Error.Conflict(PlantIdentificationErrorCodes.QuotaExceeded, "Pl@ntNet's daily allowance is spent.");

                case HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden:
                    LogRejected(logger, (int)response.StatusCode);
                    return Error.Unexpected(PlantIdentificationErrorCodes.ServiceFailed, "Pl@ntNet refused the API key.");
            }

            if (!response.IsSuccessStatusCode)
            {
                LogFailedStatus(logger, (int)response.StatusCode);
                return Error.Unexpected(PlantIdentificationErrorCodes.ServiceFailed, $"Pl@ntNet answered {(int)response.StatusCode}.");
            }

            var body = await response.Content.ReadFromJsonAsync<IdentifyResponseDto>(Json, cancellationToken).ConfigureAwait(false);

            var species = body?.Results?
                .Where(result => !string.IsNullOrWhiteSpace(result.Species?.ScientificNameWithoutAuthor))
                .Select(result => new PlantCandidate(
                    result.Species!.ScientificNameWithoutAuthor!.Trim(),
                    NullIfEmpty(result.Species.ScientificNameAuthorship),
                    NullIfEmpty(result.Species.Genus?.ScientificNameWithoutAuthor),
                    NullIfEmpty(result.Species.Family?.ScientificNameWithoutAuthor),
                    result.Species.CommonNames?.Where(name => !string.IsNullOrWhiteSpace(name)).Select(name => name.Trim()).ToList() ?? [],
                    Math.Clamp(result.Score, 0d, 1d),
                    long.TryParse(result.Gbif?.Id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var gbifKey) ? gbifKey : null))
                .OrderByDescending(candidate => candidate.Score)
                .ToList() ?? [];

            if (species.Count == 0)
            {
                LogNotRecognised(logger);
                return Error.NotFound(PlantIdentificationErrorCodes.NotRecognised, "Pl@ntNet recognises no plant in the picture.");
            }

            var genera = body?.OtherResults?.Genus?
                .Where(result => !string.IsNullOrWhiteSpace(result.Genus?.ScientificName))
                .Select(result => new PlantGenusCandidate(
                    result.Genus!.ScientificName!.Trim(),
                    NullIfEmpty(result.Genus.Family?.ScientificNameWithoutAuthor ?? result.Genus.Family?.ScientificName),
                    result.Genus.CommonNames?.Where(name => !string.IsNullOrWhiteSpace(name)).Select(name => name.Trim()).ToList() ?? [],
                    Math.Clamp(result.Score, 0d, 1d)))
                .OrderByDescending(genus => genus.Score)
                .ToList() ?? [];

            LogIdentified(logger, species[0].ScientificName, species[0].Score, pictures.Count, body?.RemainingIdentificationRequests);
            return new PlantIdentification(species, body?.RemainingIdentificationRequests) { Genera = genera };
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or NotSupportedException
                                              || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            LogUnavailable(logger, exception);
            return Error.Unexpected(PlantIdentificationErrorCodes.ServiceFailed, "Pl@ntNet could not be reached.");
        }
    }

    /// <summary>The picture's bytes and type, or why it cannot be sent.</summary>
    private static async Task<Result<Picture>> DownloadAsync(
        HttpClient client,
        string imageUrl,
        AiPlantIdentificationConfiguration settings,
        CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(imageUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return Error.Validation(PlantIdentificationErrorCodes.ImageUnusable, $"The picture could not be fetched ({(int)response.StatusCode}).");
        }

        var contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
        if (!AcceptedContentTypes.Contains(contentType) && !ConvertedContentTypes.Contains(contentType))
        {
            return Error.Validation(PlantIdentificationErrorCodes.ImageUnusable, $"Pl@ntNet takes JPEG and PNG, not '{contentType}'.");
        }

        if (response.Content.Headers.ContentLength is { } length && length > settings.MaxImageBytes)
        {
            return Error.Validation(PlantIdentificationErrorCodes.ImageUnusable, $"The picture is {length:N0} bytes, over the limit.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[64 * 1024];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > settings.MaxImageBytes)
            {
                return Error.Validation(PlantIdentificationErrorCodes.ImageUnusable, "The picture is over the size limit.");
            }

            buffer.Write(chunk, 0, read);
        }

        if (ConvertedContentTypes.Contains(contentType))
        {
            return ToJpeg(buffer.ToArray()) is { } jpeg
                ? new Picture(jpeg, "image/jpeg")
                : Error.Validation(PlantIdentificationErrorCodes.ImageUnusable, $"The '{contentType}' picture could not be read.");
        }

        return new Picture(buffer.ToArray(), contentType == "image/jpg" ? "image/jpeg" : contentType.ToLowerInvariant());
    }

    /// <summary>The picture as a JPEG, or null when it cannot be read: a WebP, GIF (its first frame) or BMP Discord served.</summary>
    internal static byte[]? ToJpeg(byte[] picture)
    {
        try
        {
            using var bitmap = SKBitmap.Decode(picture);
            if (bitmap is null)
            {
                return null;
            }

            using var image = SKImage.FromBitmap(bitmap);
            using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, JpegQuality);
            return encoded?.ToArray();
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// Claims one of the day's identifications, or refuses: past this bot's own cap, or once the service
    /// has answered 429 today. Counted in memory: a restart forgets the count, which is why the cap sits
    /// under the service's own.
    /// </summary>
    private bool TryClaimRequest(DateOnly today, int cap)
    {
        lock (_quotaLock)
        {
            if (_countedDay != today)
            {
                _countedDay = today;
                _requestsToday = 0;
                _serviceExhaustedToday = false;
            }

            if (_serviceExhaustedToday || _requestsToday >= cap)
            {
                return false;
            }

            _requestsToday++;
            return true;
        }
    }

    private void MarkServiceExhausted()
    {
        lock (_quotaLock)
        {
            _serviceExhaustedToday = true;
        }
    }

    /// <summary>A Discord attachment's link carries a signature in its query that changes between reads; the path names the file.</summary>
    private static string CacheKeyOf(string imageUrl) =>
        Uri.TryCreate(imageUrl, UriKind.Absolute, out var url) ? url.GetLeftPart(UriPartial.Path) : imageUrl;

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record Picture(byte[] Bytes, string ContentType);

    private sealed record CachedAnswer(Result<PlantIdentification> Answer, DateTimeOffset ExpiresAt);

    private sealed class IdentifyResponseDto
    {
        [JsonPropertyName("results")]
        public List<ResultDto>? Results { get; set; }

        [JsonPropertyName("remainingIdentificationRequests")]
        public int? RemainingIdentificationRequests { get; set; }

        [JsonPropertyName("otherResults")]
        public OtherResultsDto? OtherResults { get; set; }
    }

    /// <summary>The genera (and families) scored on their own, given with <c>detailed=true</c>.</summary>
    private sealed class OtherResultsDto
    {
        [JsonPropertyName("genus")]
        public List<GenusResultDto>? Genus { get; set; }
    }

    private sealed class GenusResultDto
    {
        [JsonPropertyName("score")]
        public double Score { get; set; }

        [JsonPropertyName("genus")]
        public GenusDto? Genus { get; set; }
    }

    private sealed class GenusDto
    {
        [JsonPropertyName("scientificName")]
        public string? ScientificName { get; set; }

        [JsonPropertyName("family")]
        public FamilyDto? Family { get; set; }

        [JsonPropertyName("commonNames")]
        public List<string>? CommonNames { get; set; }
    }

    private sealed class FamilyDto
    {
        [JsonPropertyName("scientificNameWithoutAuthor")]
        public string? ScientificNameWithoutAuthor { get; set; }

        [JsonPropertyName("scientificName")]
        public string? ScientificName { get; set; }
    }

    private sealed class ResultDto
    {
        [JsonPropertyName("score")]
        public double Score { get; set; }

        [JsonPropertyName("species")]
        public SpeciesDto? Species { get; set; }

        [JsonPropertyName("gbif")]
        public GbifDto? Gbif { get; set; }
    }

    private sealed class SpeciesDto
    {
        [JsonPropertyName("scientificNameWithoutAuthor")]
        public string? ScientificNameWithoutAuthor { get; set; }

        [JsonPropertyName("scientificNameAuthorship")]
        public string? ScientificNameAuthorship { get; set; }

        [JsonPropertyName("genus")]
        public NamedDto? Genus { get; set; }

        [JsonPropertyName("family")]
        public NamedDto? Family { get; set; }

        [JsonPropertyName("commonNames")]
        public List<string>? CommonNames { get; set; }
    }

    private sealed class NamedDto
    {
        [JsonPropertyName("scientificNameWithoutAuthor")]
        public string? ScientificNameWithoutAuthor { get; set; }
    }

    private sealed class GbifDto
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }
    }

    [LoggerMessage(LogLevel.Information, "Pl@ntNet names the plant of {Pictures} picture(s) {ScientificName} at {Score:F2}; {Remaining} identification(s) left today.")]
    static partial void LogIdentified(ILogger logger, string scientificName, double score, int pictures, int? remaining);

    [LoggerMessage(LogLevel.Information, "Pl@ntNet recognises no plant in the picture.")]
    static partial void LogNotRecognised(ILogger logger);

    [LoggerMessage(LogLevel.Warning, "Pl@ntNet refused the request ({Status}); check AI:Tools:PlantIdentification:ApiKey.")]
    static partial void LogRejected(ILogger logger, int status);

    [LoggerMessage(LogLevel.Warning, "Pl@ntNet answered {Status}; the picture's plant is not identified.")]
    static partial void LogFailedStatus(ILogger logger, int status);

    [LoggerMessage(LogLevel.Warning, "Pl@ntNet could not be reached; the picture's plant is not identified.")]
    static partial void LogUnavailable(ILogger logger, Exception exception);

    [LoggerMessage(LogLevel.Warning, "Pl@ntNet's daily allowance is spent (429); no more identifications until 00:00 UTC.")]
    static partial void LogServiceQuota(ILogger logger);

    [LoggerMessage(LogLevel.Information, "This bot's {Cap} Pl@ntNet identifications for today are spent; no more until 00:00 UTC.")]
    static partial void LogQuotaSpent(ILogger logger, int cap);
}
