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
    /// <summary>Route the relay controller serves; the public URL is this appended to the base.</summary>
    public const string RoutePrefix = "api/v1/ai/images";

    /// <summary>
    /// Only image types are stored. An allowlist rather than a blocklist because these bytes are later
    /// served back from our own origin, and the type we declare is the type a browser will trust.
    /// </summary>
    private static readonly Dictionary<string, string> ExtensionsByContentType = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/png"] = ".png",
        ["image/jpeg"] = ".jpg",
        ["image/jpg"] = ".jpg",
        ["image/gif"] = ".gif",
        ["image/webp"] = ".webp"
    };

    private static readonly Dictionary<string, string> ContentTypesByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".webp"] = "image/webp"
    };

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

            using var request = new HttpRequestMessage(HttpMethod.Get, imageUrl);

            // Sites that refuse unattended clients typically check the referer, so it is sent as the
            // page the image belongs to. The user agent still identifies the bot honestly.
            request.Headers.Referrer = new Uri(imageUrl).GetLeftPart(UriPartial.Authority) is { } origin
                ? new Uri(origin)
                : null;

            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
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

        return $"{settings.PublicBaseUrl!.TrimEnd('/')}/{RoutePrefix}/{hash}{extension}";
    }

    public Task<RelayedImageContent?> ReadAsync(string hash, CancellationToken cancellationToken = default)
    {
        // Anything not exactly a hash plus a known extension is refused before touching the disk, so
        // no caller-supplied text ever reaches a path.
        if (!TrySplitName(hash, out var digest, out var extension))
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
            new RelayedImageContent(content, ContentTypesByExtension[extension]));
    }

    /// <summary>Name of the HttpClient used to fetch images, configured with the polite content pipeline.</summary>
    public const string HttpClientName = "AiImageRelay";

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

        return contentType is not null && ExtensionsByContentType.TryGetValue(contentType, out var extension)
            ? extension
            : null;
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

    /// <summary>Splits a served name into its digest and extension, rejecting anything malformed.</summary>
    private static bool TrySplitName(string name, out string digest, out string extension)
    {
        digest = string.Empty;
        extension = string.Empty;

        var dot = name.LastIndexOf('.');
        if (dot != 64 || name.Length > 70)
        {
            return false;
        }

        digest = name[..dot];
        extension = name[dot..];

        return digest.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f')
               && ContentTypesByExtension.ContainsKey(extension);
    }

    [LoggerMessage(LogLevel.Debug, "Host refused an image fetch for {ImageUrl} (HTTP {StatusCode}); keeping the original link.")]
    static partial void LogRelayRefused(ILogger logger, string imageUrl, int statusCode);

    [LoggerMessage(LogLevel.Debug, "Could not relay {ImageUrl}; keeping the original link.")]
    static partial void LogRelayFailed(ILogger logger, string imageUrl, Exception exception);
}
