using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.OutputPorts.AI;
using Utilities;

namespace Infrastructure.OutputAdapters.AI.ImageRelay;

/// <summary>
/// Stores relayed images on disk, addressed by the SHA-256 of their content.
///
/// Content addressing does three jobs at once: the same image fetched from two guides is stored once,
/// a stored file can never be overwritten with different bytes, and the served path contains nothing
/// caller-supplied, so there is no path-traversal surface to get wrong.
/// </summary>
public sealed partial class FileSystemImageRelay(
    IHttpClientFactory httpClientFactory,
    IOptions<AiImageRelayConfiguration> configuration,
    ILogger<FileSystemImageRelay> logger) : IImageRelay
{
    /// <summary>Hops allowed while chasing a redirect chain, before it is treated as a refusal.</summary>
    private const int MaxRedirects = 5;

    public bool IsEnabled => !string.IsNullOrWhiteSpace(configuration.Value.PublicBaseUrl);

    public async Task<string> ResolveAsync(string imageUrl, CancellationToken cancellationToken = default)
    {
        if (!IsEnabled || !ShouldRelay(imageUrl))
        {
            return imageUrl;
        }

        try
        {
            var client = httpClientFactory.CreateClient(HttpClientName);
            var current = new Uri(imageUrl);

            // Redirects are followed by hand rather than by the handler, because the referer has to be
            // rewritten at every hop. These hosts send a request for www.example.net on to a regional
            // mirror such as de.example.net, and then refuse it: the check is same-host, and a referer
            // still naming the site the redirect came from reads as hotlinking. Measured on plonkit.net,
            // where a matching referer is the whole difference between 200 and 403.
            for (var hop = 0; ; hop++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, current);
                request.Headers.Referrer = new Uri(current.GetLeftPart(UriPartial.Authority));

                using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);

                if (IsRedirect(response) && response.Headers.Location is { } location)
                {
                    if (hop == MaxRedirects)
                    {
                        LogRelayRefused(logger, imageUrl, (int)response.StatusCode);
                        return imageUrl;
                    }

                    // Location is allowed to be relative, and is resolved against the hop it came from.
                    current = new Uri(current, location);
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    LogRelayRefused(logger, imageUrl, (int)response.StatusCode);
                    return imageUrl;
                }

                var content = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
                var stored = await StoreAsync(content, response.Content.Headers.ContentType?.MediaType, cancellationToken)
                    .ConfigureAwait(false);

                return stored.IsSuccess ? stored.Value : imageUrl;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UriFormatException)
        {
            // An unrelayable image costs a picture, not the source it belongs to.
            LogRelayFailed(logger, imageUrl, ex);
            return imageUrl;
        }
    }

    public async Task<Result<string>> StoreAsync(
        byte[] content,
        string? contentType,
        CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
        {
            return Error.Conflict("ai.image_relay_disabled", "The image relay has no public base URL configured.");
        }

        var settings = configuration.Value;

        if (content.Length == 0 || content.Length > settings.MaxImageBytes)
        {
            return Error.Validation("ai.image_too_large",
                $"Image is {content.Length} bytes; the limit is {settings.MaxImageBytes}.");
        }

        var extension = ResolveExtension(contentType, content);
        if (extension is null)
        {
            return Error.Validation("ai.image_unsupported_type", $"Unsupported image type '{contentType}'.");
        }

        var hash = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
        var path = BuildPath(hash, extension);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // Identical content produces the same path, so an existing file is already correct.
        if (!File.Exists(path))
        {
            await File.WriteAllBytesAsync(path, content, cancellationToken).ConfigureAwait(false);
        }

        return RelayedImageUrl.Build(settings.PublicBaseUrl!, $"{hash}{extension}");
    }

    public Task<RelayedImageContent?> ReadAsync(string name, CancellationToken cancellationToken = default)
    {
        // Anything not exactly a hash plus a known extension is refused before touching the disk, so
        // no caller-supplied text ever reaches a path.
        if (!RelayedImageUrl.TrySplitName(name, out var digest, out var extension))
        {
            return Task.FromResult<RelayedImageContent?>(null);
        }

        var path = BuildPath(digest, extension);
        if (!File.Exists(path))
        {
            return Task.FromResult<RelayedImageContent?>(null);
        }

        Stream content = File.OpenRead(path);
        return Task.FromResult<RelayedImageContent?>(
            new RelayedImageContent(content, RelayedImageUrl.ContentTypeFor(extension)!));
    }

    public async Task<string?> ReadAsDataUrlAsync(string imageUrl, CancellationToken cancellationToken = default)
    {
        if (!RelayedImageUrl.TryReadName(imageUrl, out var name)
            || !RelayedImageUrl.TrySplitName(name, out var digest, out var extension))
        {
            return null;
        }

        var path = BuildPath(digest, extension);
        if (!File.Exists(path))
        {
            return null;
        }

        var content = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        return $"data:{RelayedImageUrl.ContentTypeFor(extension)};base64,{Convert.ToBase64String(content)}";
    }

    public async Task<ImageRelayReachability> CheckPublicReachabilityAsync(CancellationToken cancellationToken = default)
    {
        var settings = configuration.Value;
        if (string.IsNullOrWhiteSpace(settings.PublicBaseUrl))
        {
            return new ImageRelayReachability(ImageRelayReachabilityStatus.NotConfigured, null, null);
        }

        var baseUrl = settings.PublicBaseUrl.Trim().TrimEnd('/');
        if (!Uri.TryCreate($"{baseUrl}/health", UriKind.Absolute, out var healthUrl)
            || healthUrl.Scheme is not ("https" or "http"))
        {
            return new ImageRelayReachability(ImageRelayReachabilityStatus.Unreachable, baseUrl, "not an http(s) URL");
        }

        var timeoutSeconds = Math.Max(1, settings.ReachabilityTimeoutSeconds);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var client = httpClientFactory.CreateClient(ReachabilityHttpClientName);
            using var response = await client
                .GetAsync(healthUrl, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                .ConfigureAwait(false);

            return response.IsSuccessStatusCode
                ? new ImageRelayReachability(ImageRelayReachabilityStatus.Reachable, baseUrl,
                    $"answered in {stopwatch.ElapsedMilliseconds} ms")
                : new ImageRelayReachability(ImageRelayReachabilityStatus.Unreachable, baseUrl,
                    $"answered HTTP {(int)response.StatusCode}");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ImageRelayReachability(ImageRelayReachabilityStatus.Unreachable, baseUrl,
                $"no answer within {timeoutSeconds} s");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A name that no longer resolves lands here, in the resolver's own words, which is what
            // tells a lost DNS record apart from the bot being down.
            LogReachabilityFailed(logger, baseUrl, ex);
            return new ImageRelayReachability(ImageRelayReachabilityStatus.Unreachable, baseUrl, Describe(ex));
        }
    }

    /// <summary>
    /// The outer message names the host and port ("Name or service not known (host:443)"), except for a
    /// TLS failure, whose outer message only says to read the inner one.
    /// </summary>
    private static string Describe(Exception exception) =>
        exception.InnerException is { } inner
        && exception.Message.Contains("inner exception", StringComparison.OrdinalIgnoreCase)
            ? inner.Message
            : exception.Message;

    /// <summary>Name of the HttpClient used to fetch images, configured with the polite content pipeline.</summary>
    public const string HttpClientName = "AiImageRelay";

    /// <summary>
    /// Name of the HttpClient that asks the public base URL for its health. Deliberately not
    /// registered, so it gets the defaults: no resilience pipeline, because a retry would hide exactly
    /// the failure the check is looking for, and no politeness queue to wait behind.
    /// </summary>
    public const string ReachabilityHttpClientName = "AiImageRelayReachability";

    private static bool IsRedirect(HttpResponseMessage response) =>
        response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Found
            or HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;

    private bool ShouldRelay(string imageUrl)
    {
        if (!Uri.TryCreate(imageUrl, UriKind.Absolute, out var uri))
        {
            return false;
        }

        return configuration.Value.RelayHosts.Any(host =>
            uri.Host.Equals(host, StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith($".{host}", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Sharded one level by the hash's first byte. A flat directory of tens of thousands of files is
    /// slow to enumerate on most filesystems.
    /// </summary>
    private string BuildPath(string hash, string extension) =>
        Path.Combine(configuration.Value.Directory, hash[..2], $"{hash}{extension}");

    /// <summary>
    /// Trusts the sniffed bytes over the declared type. Guide hosts routinely serve PNGs as
    /// octet-stream, and the declared type is the one a browser would act on later.
    /// </summary>
    private static string? ResolveExtension(string? contentType, byte[] content)
    {
        if (SniffExtension(content) is { } sniffed)
        {
            return sniffed;
        }

        return RelayedImageUrl.ExtensionFor(contentType);
    }

    private static string? SniffExtension(byte[] content)
    {
        if (content.Length >= 8 && content[0] == 0x89 && content[1] == 0x50 && content[2] == 0x4E && content[3] == 0x47)
        {
            return ".png";
        }

        if (content.Length >= 3 && content[0] == 0xFF && content[1] == 0xD8 && content[2] == 0xFF)
        {
            return ".jpg";
        }

        if (content.Length >= 6 && content[0] == 0x47 && content[1] == 0x49 && content[2] == 0x46)
        {
            return ".gif";
        }

        // "RIFF"…"WEBP"
        return content.Length >= 12 && content[0] == 0x52 && content[1] == 0x49
                                    && content[8] == 0x57 && content[9] == 0x45
            ? ".webp"
            : null;
    }

    [LoggerMessage(LogLevel.Debug, "Host refused an image fetch for {ImageUrl} (HTTP {StatusCode}); keeping the original link.")]
    static partial void LogRelayRefused(ILogger logger, string imageUrl, int statusCode);

    [LoggerMessage(LogLevel.Debug, "Could not relay {ImageUrl}; keeping the original link.")]
    static partial void LogRelayFailed(ILogger logger, string imageUrl, Exception exception);

    [LoggerMessage(LogLevel.Information, "The image relay's public base URL {PublicBaseUrl} did not answer its health check.")]
    static partial void LogReachabilityFailed(ILogger logger, string publicBaseUrl, Exception exception);
}
