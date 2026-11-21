using Microsoft.Extensions.Logging;
using UseCases.OutputPorts.AI;
using UseCases.UseCases.AI.StreetView;

namespace Infrastructure.OutputAdapters.AI.StreetView;

/// <summary>
/// Follows a shortened Google Maps link by hand: the named client follows no redirect itself, so each
/// hop is checked before it is taken. Only Google's own shorteners are asked, and only a hop to another
/// short link or to Google Maps is followed; anything else ends the walk. The shortener answers 302
/// with the full link (read 2026-11-21: <c>maps.app.goo.gl/…</c> → <c>google.com/maps/@…/data=…</c>), or
/// 404 for a link that does not exist.
/// </summary>
public sealed partial class GoogleMapsLinkResolver(
    IHttpClientFactory httpClientFactory,
    ILogger<GoogleMapsLinkResolver> logger) : IMapsLinkResolver
{
    public const string HttpClientName = "MapsLinks";

    private const int MaxHops = 3;

    public async Task<string?> ResolveAsync(string shortLink, CancellationToken cancellationToken = default)
    {
        if (!StreetViewLink.IsShortLink(shortLink))
        {
            return null;
        }

        var client = httpClientFactory.CreateClient(HttpClientName);
        var current = shortLink.Trim();
        try
        {
            for (var hop = 0; hop < MaxHops; hop++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, current);
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                var status = (int)response.StatusCode;
                if (status is < 300 or >= 400 || response.Headers.Location is not { } location)
                {
                    LogNotRedirected(logger, status, shortLink);
                    return null;
                }

                var next = (location.IsAbsoluteUri ? location : new Uri(new Uri(current), location)).AbsoluteUri;
                if (StreetViewLink.IsMapsLink(next))
                {
                    return next;
                }

                if (!StreetViewLink.IsShortLink(next))
                {
                    LogLeftGoogle(logger, shortLink);
                    return null;
                }

                current = next;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogFailed(logger, exception, shortLink);
        }

        return null;
    }

    [LoggerMessage(LogLevel.Information, "The Maps link {Link} did not lead on ({Status}).")]
    static partial void LogNotRedirected(ILogger logger, int status, string link);

    [LoggerMessage(LogLevel.Warning, "The Maps link {Link} led away from Google Maps; it was not followed.")]
    static partial void LogLeftGoogle(ILogger logger, string link);

    [LoggerMessage(LogLevel.Warning, "The Maps link {Link} could not be followed.")]
    static partial void LogFailed(ILogger logger, Exception exception, string link);
}
