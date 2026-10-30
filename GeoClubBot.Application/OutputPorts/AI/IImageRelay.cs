using Utilities;

namespace UseCases.OutputPorts.AI;

/// <param name="ContentType">Serving the right type matters: providers reject an image sent as octet-stream.</param>
public sealed record RelayedImageContent(Stream Content, string ContentType);

public enum ImageRelayReachabilityStatus
{
    /// <summary>No public base URL is set, so there is nothing to reach.</summary>
    NotConfigured,

    /// <summary>The public base URL's health endpoint answered with a success status.</summary>
    Reachable,

    /// <summary>The name did not resolve, the connection failed, the answer was an error, or none came in time.</summary>
    Unreachable
}

/// <param name="PublicBaseUrl">The address that was tried; null when none is configured.</param>
/// <param name="Detail">What happened, for an operator: how long it took, or why it failed.</param>
public sealed record ImageRelayReachability(
    ImageRelayReachabilityStatus Status,
    string? PublicBaseUrl,
    string? Detail);

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

    /// <summary>
    /// Reads a stored image back by its stored name (<c>{sha256}{extension}</c>, see
    /// <see cref="RelayedImageUrl.TryReadName"/>). Null when the name is malformed or not on disk.
    ///
    /// Used for serving, and for uploading a picture with an answer: an upload carries the bytes, so the
    /// picture shows even when Discord cannot reach the public URL. The caller disposes the stream.
    /// </summary>
    Task<RelayedImageContent?> ReadAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// A relayed image as a <c>data:</c> URL, so the AI provider receives the bytes instead of fetching
    /// them back from this host. Null when <paramref name="imageUrl"/> is not a relayed image or its file
    /// is no longer on disk.
    ///
    /// Sending the bytes removes the one thing indexing needed this host to be for the provider: reachable
    /// at the moment the job runs. Through a home tunnel in the early morning it intermittently was not,
    /// and every image in the batch was lost with it. Answers do the same for Discord by uploading the file.
    /// </summary>
    Task<string?> ReadAsDataUrlAsync(string imageUrl, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asks the public base URL's <c>/health</c> whether this host answers there, with a short timeout.
    /// Never throws for an unreachable host: it feeds a status line, and a status command that fails is
    /// worse than one saying "unreachable".
    ///
    /// A failure is conclusive; a success proves less. The request leaves from the bot's own host, whose
    /// resolver may answer for the name privately — MagicDNS does for a tailnet name — even while the
    /// public record Discord depends on is gone.
    /// </summary>
    Task<ImageRelayReachability> CheckPublicReachabilityAsync(CancellationToken cancellationToken = default);
}
