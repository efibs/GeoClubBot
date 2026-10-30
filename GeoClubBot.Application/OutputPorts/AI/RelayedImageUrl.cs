using System.Diagnostics.CodeAnalysis;

namespace UseCases.OutputPorts.AI;

/// <summary>
/// The shape of a relayed image's URL: <c>{PublicBaseUrl}/api/v1/ai/images/{sha256}{extension}</c>.
///
/// Pure and shared, because three layers have to agree on it: the relay writes these URLs, the API
/// serves the route, and the Discord side recognises them to upload the bytes instead of trusting the
/// host. Kept off <see cref="IImageRelay"/> on purpose: the integration test host substitutes every
/// port it does not know, and a substituted string method answers <c>""</c> — every picture URL would
/// silently become empty there.
/// </summary>
public static class RelayedImageUrl
{
    /// <summary>Route the relay controller serves; the public URL is this appended to the base.</summary>
    public const string RoutePrefix = "api/v1/ai/images";

    /// <summary>Length of a lowercase hex SHA-256 digest, the whole of a stored name before its extension.</summary>
    private const int DigestLength = 64;

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

    /// <summary>The public URL of a stored name, under <paramref name="publicBaseUrl"/>.</summary>
    public static string Build(string publicBaseUrl, string name) =>
        $"{publicBaseUrl.TrimEnd('/')}/{RoutePrefix}/{name}";

    /// <summary>
    /// Takes the stored name (<c>{sha256}{extension}</c>) out of a relayed URL.
    ///
    /// Matched on the route rather than the host, so images indexed under an earlier public base URL are
    /// still found on disk after the bot's address changes — a quick tunnel mints a new one every run.
    /// Nothing else in the URL is trusted: the name must still pass <see cref="TrySplitName"/>, so no
    /// caller-supplied text can reach a path.
    /// </summary>
    public static bool TryReadName(string? imageUrl, [NotNullWhen(true)] out string? name)
    {
        name = null;

        if (!Uri.TryCreate(imageUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http"))
        {
            return false;
        }

        var marker = $"/{RoutePrefix}/";
        var start = uri.AbsolutePath.LastIndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
        {
            return false;
        }

        var candidate = uri.AbsolutePath[(start + marker.Length)..];
        if (!TrySplitName(candidate, out _, out _))
        {
            return false;
        }

        name = candidate;
        return true;
    }

    /// <summary>Splits a served name into its digest and extension, rejecting anything malformed.</summary>
    public static bool TrySplitName(string? name, out string digest, out string extension)
    {
        digest = string.Empty;
        extension = string.Empty;

        if (name is null)
        {
            return false;
        }

        var dot = name.LastIndexOf('.');
        if (dot != DigestLength || name.Length > DigestLength + 6)
        {
            return false;
        }

        var digestPart = name[..dot];
        var extensionPart = name[dot..];

        if (!digestPart.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f')
            || !ContentTypesByExtension.ContainsKey(extensionPart))
        {
            return false;
        }

        digest = digestPart;
        extension = extensionPart;
        return true;
    }

    /// <summary>
    /// The same relayed image under the public base URL configured now, or <paramref name="imageUrl"/>
    /// unchanged when it is not a relayed image or there is no base to move it to.
    ///
    /// A stored URL keeps the host it was indexed under. When that host stops resolving — a tunnel
    /// restarted with a new name, or, on 2026-10-22, Tailscale no longer publishing the funnel's DNS —
    /// showing it as-is guarantees a broken picture, while the current base is the one address this bot
    /// can still be expected to answer on.
    /// </summary>
    public static string Rebase(string imageUrl, string? publicBaseUrl)
    {
        // A base without a scheme ("host.tailnet.ts.net") would turn a working https link into one
        // Discord rejects outright, taking the whole answer with it — the stored link is the safer bet.
        if (string.IsNullOrWhiteSpace(publicBaseUrl)
            || !Uri.TryCreate(publicBaseUrl.Trim(), UriKind.Absolute, out var baseUri)
            || baseUri.Scheme is not ("http" or "https")
            || !TryReadName(imageUrl, out var name))
        {
            return imageUrl;
        }

        return Build(publicBaseUrl.Trim(), name);
    }

    /// <summary>The extension a stored image of this type is named with; null for a type the relay refuses.</summary>
    public static string? ExtensionFor(string? contentType) =>
        contentType is not null && ExtensionsByContentType.TryGetValue(contentType, out var extension)
            ? extension
            : null;

    /// <summary>The type a stored name is served as; null for an extension the relay never writes.</summary>
    public static string? ContentTypeFor(string extension) =>
        ContentTypesByExtension.TryGetValue(extension, out var contentType) ? contentType : null;
}
