using Utilities;

namespace UseCases.OutputPorts.AI;

/// <param name="ContentType">Serving the right type matters: providers reject an image sent as octet-stream.</param>
public sealed record RelayedImageContent(Stream Content, string ContentType);

/// <summary>
/// Re-hosts guide images that their own host will not serve to an unattended client.
///
/// Deliberately not a proxy. Bytes are copied once while indexing and served afterwards by content
/// hash; there is no path that fetches an arbitrary URL on request, which would make the endpoint an
/// open proxy and a way to reach anything the bot's network can see.
/// </summary>
public interface IImageRelay
{
    /// <summary>False when no public base URL is configured, in which case images are left as they are.</summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Returns a URL the AI provider can fetch for <paramref name="imageUrl"/> — either a relayed copy
    /// or the original, unchanged. Never fails: an image that cannot be relayed is worth less than the
    /// source it belongs to, so the original URL is returned and indexing continues.
    /// </summary>
    Task<string> ResolveAsync(string imageUrl, CancellationToken cancellationToken = default);

    /// <summary>Stores bytes that have no public URL of their own, such as an image embedded in a document.</summary>
    Task<Result<string>> StoreAsync(byte[] content, string? contentType, CancellationToken cancellationToken = default);

    /// <summary>Reads a stored image back for serving. Null when the hash is unknown.</summary>
    Task<RelayedImageContent?> ReadAsync(string hash, CancellationToken cancellationToken = default);

    /// <summary>
    /// A relayed image as a <c>data:</c> URL, so the AI provider receives the bytes instead of fetching
    /// them back from this host. Null when <paramref name="imageUrl"/> is not a relayed image or its file
    /// is no longer on disk.
    ///
    /// Sending the bytes removes the one thing indexing needed this host to be for the provider: reachable
    /// at the moment the job runs. Through a home tunnel in the early morning it intermittently was not,
    /// and every image in the batch was lost with it. Discord still shows the public URL.
    /// </summary>
    Task<string?> ReadAsDataUrlAsync(string imageUrl, CancellationToken cancellationToken = default);
}
