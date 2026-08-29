using System.Text.RegularExpressions;

namespace UseCases.UseCases.AI.Ingestion;

/// <param name="UnsupportedReason">Set when the link is understood but cannot be indexed.</param>
public sealed record ClassifiedLink(string SourceType, string NaturalKey, string? UnsupportedReason = null)
{
    public bool IsSupported => UnsupportedReason is null;
}

/// <summary>
/// Decides which extractor family a link belongs to.
///
/// Links that cannot be indexed are classified too, rather than dropped, so the source registry can
/// say how much of a library is covered and why the rest is not. Silently ignoring them would make a
/// half-indexed library look complete.
/// </summary>
public static partial class SourceLinkClassifier
{
    public const string PlonkIt = "plonkit";
    public const string GoogleDoc = "gdoc";
    public const string GoogleSlides = "gslides";
    public const string GoogleSheet = "gsheet";
    public const string ImgurAlbum = "imgur";
    public const string DirectImage = "image";

    /// <summary>
    /// Label for links that are catalogued but never extracted. They are recorded so the registry can
    /// account for every entry in a library rather than leaving an unexplained shortfall.
    /// </summary>
    public const string Unsupported = "unsupported";

    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".gif", ".webp"];

    public static ClassifiedLink Classify(Uri url)
    {
        ArgumentNullException.ThrowIfNull(url);

        var host = url.Host.ToLowerInvariant();

        // Refusals come first, and deliberately outrank the file-extension check below: a Discord CDN
        // attachment ends in .png but its links are signed and expire, so treating it as an indexable
        // image would fill the index with entries that are already dead.
        if (host.Contains("discord", StringComparison.Ordinal))
        {
            return new ClassifiedLink(Unsupported, url.ToString(),
                "Discord links need an authenticated session, and its CDN links expire.");
        }

        if (host.Contains("youtube.", StringComparison.Ordinal) || host is "youtu.be")
        {
            return new ClassifiedLink(Unsupported, url.ToString(),
                "Video guides carry their content in speech, which is not indexed.");
        }

        // A country page on the guide site. Anything else on that host falls through, so its images
        // are still recognised as images.
        if (host is "plonkit.net" || host.EndsWith(".plonkit.net", StringComparison.Ordinal))
        {
            var slug = url.AbsolutePath.Trim('/');
            if (slug.Length > 0 && !slug.Contains('/'))
            {
                return new ClassifiedLink(PlonkIt, slug);
            }
        }

        if (host is "docs.google.com" && ClassifyGoogleDocument(url) is { } googleDocument)
        {
            return googleDocument;
        }

        if (host is "imgur.com" or "www.imgur.com")
        {
            var match = ImgurAlbumPath().Match(url.AbsolutePath);
            if (match.Success)
            {
                return new ClassifiedLink(ImgurAlbum, match.Groups["id"].Value);
            }
        }

        return IsDirectImage(url)
            // Keyed by the full URL: a bare image has no other stable identity.
            ? new ClassifiedLink(DirectImage, url.ToString())
            : Unrecognised(url);
    }

    /// <summary>
    /// A one-off site with no dedicated extractor. Catalogued rather than dropped, so an operator can
    /// see what a library contains that the bot cannot read.
    /// </summary>
    private static ClassifiedLink Unrecognised(Uri url) =>
        new(Unsupported, url.ToString(), $"No extractor supports {url.Host}.");

    private static ClassifiedLink? ClassifyGoogleDocument(Uri url)
    {
        var match = GoogleDocumentPath().Match(url.AbsolutePath);
        if (!match.Success)
        {
            return null;
        }

        var id = match.Groups["id"].Value;

        return match.Groups["kind"].Value switch
        {
            "document" => new ClassifiedLink(GoogleDoc, id),
            "presentation" => new ClassifiedLink(GoogleSlides, id),
            "spreadsheets" => new ClassifiedLink(GoogleSheet, id),
            _ => null
        };
    }

    private static bool IsDirectImage(Uri url) =>
        ImageExtensions.Any(extension =>
            url.AbsolutePath.EndsWith(extension, StringComparison.OrdinalIgnoreCase));

    [GeneratedRegex(@"^/(?<kind>document|presentation|spreadsheets)/d/(?<id>[A-Za-z0-9_-]+)",
        RegexOptions.CultureInvariant)]
    private static partial Regex GoogleDocumentPath();

    [GeneratedRegex(@"^/(?:a|gallery)/(?:[a-z0-9-]+-)?(?<id>[A-Za-z0-9]+)/?$", RegexOptions.CultureInvariant)]
    private static partial Regex ImgurAlbumPath();
}
