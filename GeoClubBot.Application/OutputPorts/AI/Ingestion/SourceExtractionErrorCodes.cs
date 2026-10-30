namespace UseCases.OutputPorts.AI.Ingestion;

/// <summary>
/// Codes an <see cref="ISourceExtractor"/> fails with when it could not fetch a source. The ingestion
/// run acts on each differently, so the distinction is the point: a deleted document, a private one, a
/// host that is refusing us and a timeout all used to arrive as the same "unreachable", and were all
/// retried with the same backoff for ever.
/// </summary>
public static class SourceExtractionErrorCodes
{
    /// <summary>
    /// Deleted upstream (HTTP 404 or 410). Reported as <c>NotFound</c>. Google answers 410 on every
    /// endpoint of a deleted file, and keeps doing so; it was nine of production's ten nightly failures.
    /// </summary>
    public const string Gone = "ai.source_gone";

    /// <summary>
    /// Exists but is not publicly readable (HTTP 401 or 403 from a host that shares each file on its own,
    /// as Google does). Reported as <c>Forbidden</c>, and recoverable: an owner who shares the document
    /// again expects the bot to notice.
    /// </summary>
    public const string NotPublic = "ai.source_not_public";

    /// <summary>
    /// The export is more than the host will build (HTTP 413) or than ingestion will hold. Reported as
    /// <c>Validation</c>; a lighter export is usually still worth asking for.
    /// </summary>
    public const string ExportTooLarge = "ai.export_too_large";

    /// <summary>
    /// The host is refusing requests in general — it answered 429, the circuit breaker in front of it is
    /// open, or a host that publishes everything it serves answered 401 or 403. About the host, not the
    /// source, so the source is left as it was and the rest of the run stops asking that host.
    /// </summary>
    public const string HostRefused = "ai.source_host_refused";

    /// <summary>A timeout or a failure on the host's side (5xx, 408). About the moment; retried with backoff.</summary>
    public const string Unreachable = "ai.source_unreachable";
}
