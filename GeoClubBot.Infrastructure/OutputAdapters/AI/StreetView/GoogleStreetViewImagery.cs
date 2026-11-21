using System.Globalization;
using Microsoft.Extensions.Logging;
using UseCases.OutputPorts.AI;

namespace Infrastructure.OutputAdapters.AI.StreetView;

/// <summary>
/// Reads views of a panorama from the endpoint Google Maps draws its Street View thumbnails from
/// (<c>streetviewpixels-pa.googleapis.com/v1/thumbnail</c>): free, no key, the one GeoNN uses. It
/// answers 403 to a client that does not look like a browser, so the named client sends a browser's
/// User-Agent. Its pitch is positive looking down, the opposite of the game's and the Maps link's. A
/// panorama it does not know comes back as a small black picture with a success status, so a picture
/// that small is taken for none (read 2026-11-21). The widest it serves is 1024 pixels.
/// </summary>
public sealed partial class GoogleStreetViewImagery(
    IHttpClientFactory httpClientFactory,
    ILogger<GoogleStreetViewImagery> logger) : IStreetViewImagery
{
    public const string HttpClientName = "StreetViewImagery";

    /// <summary>What a browser sends, without which the endpoint answers 403.</summary>
    public const string BrowserUserAgent = "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/139.0 Safari/537.36";

    public const int Width = 1024;
    public const int Height = 576;

    /// <summary>Smaller than any real view: the black picture served for a panorama that does not exist is 2.4 KB.</summary>
    public const int MinimumBytes = 6_000;

    public async Task<IReadOnlyList<StreetViewPicture>> ReadViewsAsync(
        string panoId,
        IReadOnlyList<StreetViewAngle> angles,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(angles);
        if (string.IsNullOrWhiteSpace(panoId) || angles.Count == 0)
        {
            return [];
        }

        var client = httpClientFactory.CreateClient(HttpClientName);
        var views = await Task.WhenAll(angles.Select(angle => ReadAsync(client, panoId, angle, cancellationToken))).ConfigureAwait(false);
        var read = views.OfType<StreetViewPicture>().ToList();
        if (read.Count < angles.Count)
        {
            LogMissing(logger, panoId, angles.Count - read.Count, angles.Count);
        }

        return read;
    }

    /// <summary>The thumbnail request for one view.</summary>
    public static Uri ThumbnailUri(string panoId, StreetViewAngle angle)
    {
        ArgumentNullException.ThrowIfNull(angle);

        return new Uri(string.Create(CultureInfo.InvariantCulture,
            $"https://streetviewpixels-pa.googleapis.com/v1/thumbnail?panoid={Uri.EscapeDataString(panoId)}&cb_client=maps_sv.tactile.gps"
            + $"&w={Width}&h={Height}&yaw={Math.Round(angle.Heading, 2)}&pitch={Math.Round(0 - angle.Pitch, 2)}&thumbfov={Math.Round(angle.FieldOfView, 2)}"));
    }

    private async Task<StreetViewPicture?> ReadAsync(HttpClient client, string panoId, StreetViewAngle angle, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await client.GetAsync(ThumbnailUri(panoId, angle), cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                LogRefused(logger, (int)response.StatusCode, panoId);
                return null;
            }

            var contentType = response.Content.Headers.ContentType?.MediaType;
            if (contentType is null || !contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            return bytes.Length < MinimumBytes ? null : new StreetViewPicture(bytes, contentType, angle);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogFailed(logger, exception, panoId);
            return null;
        }
    }

    [LoggerMessage(LogLevel.Warning, "Google answered {Status} for a view of panorama {PanoId}.")]
    static partial void LogRefused(ILogger logger, int status, string panoId);

    [LoggerMessage(LogLevel.Warning, "A view of panorama {PanoId} could not be read.")]
    static partial void LogFailed(ILogger logger, Exception exception, string panoId);

    [LoggerMessage(LogLevel.Information, "{Missing} of {Asked} view(s) of panorama {PanoId} could not be read.")]
    static partial void LogMissing(ILogger logger, string panoId, int missing, int asked);
}
