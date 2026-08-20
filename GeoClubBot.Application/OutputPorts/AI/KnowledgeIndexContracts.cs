using Utilities;

namespace UseCases.OutputPorts.AI;

public enum KnowledgeChunkKind
{
    Text = 0,
    Image
}

/// <summary>
/// One indexed unit of guide content. Image chunks still carry <see cref="Text"/> — their caption plus
/// surrounding prose — because that text is how a written question finds them; matching a question
/// directly against image pixels scores far lower than matching it against the caption.
/// </summary>
public sealed record KnowledgeChunk
{
    /// <summary>Extractor family, e.g. <c>plonkit</c> or <c>imgur</c>.</summary>
    public required string SourceType { get; init; }

    /// <summary>Natural key of the document within its family, e.g. a country slug or album id.</summary>
    public required string SourceKey { get; init; }

    /// <summary>Stable key within the document. Must survive re-extraction, or re-ingest duplicates.</summary>
    public required string LocalKey { get; init; }

    public required KnowledgeChunkKind Kind { get; init; }

    /// <summary>Plain text — never HTML. Markup wastes prompt tokens and pollutes the vector.</summary>
    public required string Text { get; init; }

    public required string SourceUrl { get; init; }

    /// <summary>
    /// Absolute URL of the image, for image chunks. Must be fetchable by an unattended client: the
    /// embedding and chat providers fetch it server-side, and hosts that block non-browser clients
    /// fail the entire request rather than just that one image.
    /// </summary>
    public string? ImageUrl { get; init; }

    public string? Title { get; init; }

    public string? Country { get; init; }

    public string? SectionPath { get; init; }

    public string? Author { get; init; }

    /// <summary>Editorial weight from the source library, nudging better-regarded guides upward.</summary>
    public int Priority { get; init; }

    /// <summary>
    /// Derived, not stored: the same chunk always maps to the same point, so re-ingesting unchanged
    /// content updates in place rather than appending a duplicate.
    /// </summary>
    public Guid PointId => DeterministicGuid.FromName("point", $"{SourceType}|{SourceKey}|{Kind}|{LocalKey}");
}

/// <summary>
/// A chunk with its vectors. Text and image vectors are kept apart rather than blended: a combined
/// embedding is dominated by the image and loses the text signal, and the two similarity scales differ
/// enough that mixing them in one field buries every image below every paragraph.
/// </summary>
public sealed record KnowledgePoint(
    KnowledgeChunk Chunk,
    ReadOnlyMemory<float> TextVector,
    ReadOnlyMemory<float>? ImageVector = null);

public sealed record KnowledgeQuery
{
    /// <summary>The question, embedded. Matches chunk text and image captions.</summary>
    public ReadOnlyMemory<float>? TextVector { get; init; }

    /// <summary>An attached image, embedded. Matches stored images directly.</summary>
    public ReadOnlyMemory<float>? ImageVector { get; init; }

    public string? Country { get; init; }

    public string? SourceType { get; init; }

    public int Limit { get; init; } = 8;
}

public sealed record KnowledgeHit(
    Guid Id,
    float Score,
    KnowledgeChunkKind Kind,
    string Text,
    string SourceUrl,
    string? ImageUrl,
    string? Title,
    string? Country,
    string? SectionPath,
    string? Author,
    int Priority);

/// <summary>
/// The vector store holding all indexed guide content.
/// </summary>
public interface IKnowledgeIndex
{
    Task EnsureCollectionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes points, stamping each with <paramref name="ingestRun"/>. Idempotent for unchanged
    /// content because point ids are derived from the chunk's natural key.
    /// </summary>
    Task UpsertAsync(IReadOnlyList<KnowledgePoint> points, string ingestRun, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<KnowledgeHit>> SearchAsync(KnowledgeQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes points of a source that the latest run did not re-produce, identified by carrying a
    /// different <paramref name="ingestRun"/>. Deleting after writing rather than before means the
    /// index is never briefly empty, so queries stay correct throughout a re-ingest.
    /// </summary>
    Task<int> SweepAsync(string sourceType, string sourceKey, string ingestRun, CancellationToken cancellationToken = default);

    Task<long> CountAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> ListCountriesAsync(CancellationToken cancellationToken = default);
}
