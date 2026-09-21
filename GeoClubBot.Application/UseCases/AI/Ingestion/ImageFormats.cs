namespace UseCases.UseCases.AI.Ingestion;

/// <summary>
/// Which image formats survive the trip from a guide into an answer.
///
/// The embedding provider fetches image URLs itself and refuses anything it cannot decode as a
/// picture — and a refusal fails the whole request it travelled in, which is then narrowed one
/// request at a time to find the culprit. A single vector illustration in one guide therefore cost
/// a night's run several of the day's allowance and produced a warning per source. Discord will not
/// render these either, so an unusable format is dropped while indexing rather than stored and
/// worked around at every later step.
/// </summary>
public static class ImageFormats
{
    /// <summary>Formats the provider embeds and Discord renders.</summary>
    private static readonly string[] Supported = [".png", ".jpg", ".jpeg", ".gif", ".webp"];

    /// <summary>
    /// Formats known to be refused. Vector images are the case that actually occurs — rmrg.me serves
    /// its annotation overlays and symbol illustrations as <c>.svgz</c> — the rest are formats a guide
    /// links occasionally and which no part of this pipeline can use either.
    /// </summary>
    private static readonly string[] Refused =
        [".svg", ".svgz", ".avif", ".bmp", ".ico", ".tif", ".tiff", ".heic", ".heif", ".pdf"];

    /// <summary>Whether a path names a format we know is usable, for recognising a bare image link.</summary>
    public static bool IsSupportedPath(string path) =>
        Supported.Any(extension => path.EndsWith(extension, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Whether this image is worth sending to the embedding provider.
    ///
    /// Only formats known to be refused are turned away, rather than demanding a known-good extension:
    /// plenty of hosts serve ordinary JPEGs from URLs that end in no extension at all, and refusing
    /// those would quietly cost far more pictures than the broken ones save.
    /// </summary>
    public static bool CanEmbed(string? imageUrl)
    {
        if (string.IsNullOrWhiteSpace(imageUrl))
        {
            return false;
        }

        // Inline images come from the relay, which stores raster formats only — checked anyway so the
        // guard stays total rather than resting on that staying true.
        if (imageUrl.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return imageUrl.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase)
                   && !imageUrl.StartsWith("data:image/svg", StringComparison.OrdinalIgnoreCase);
        }

        var path = ReadPath(imageUrl);

        return !Refused.Any(extension => path.EndsWith(extension, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The path part alone. The extension is what names the format, and these URLs carry a
    /// cache-busting query after it — <c>…/hinduism-2.svgz?v=1a2b</c> ends in no extension at all
    /// until the query is taken off.
    /// </summary>
    private static string ReadPath(string imageUrl)
    {
        if (Uri.TryCreate(imageUrl, UriKind.Absolute, out var uri))
        {
            return uri.AbsolutePath;
        }

        var end = imageUrl.AsSpan().IndexOfAny('?', '#');
        return end < 0 ? imageUrl : imageUrl[..end];
    }
}
