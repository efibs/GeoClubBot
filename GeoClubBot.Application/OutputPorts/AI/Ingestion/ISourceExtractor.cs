using Utilities;

namespace UseCases.OutputPorts.AI.Ingestion;

/// <summary>Identity and metadata of a source, without the ingestion bookkeeping the entity carries.</summary>
/// <param name="UnsupportedReason">
/// Set when a catalogue knows about this source but it cannot be indexed — a video, or a link behind
/// authentication. Recorded rather than dropped so coverage reporting stays honest about the gap.
/// </param>
public sealed record SourceDescriptor(
    string SourceType,
    string NaturalKey,
    Uri Url,
    string? Title = null,
    string? Country = null,
    string? Continent = null,
    string? Author = null,
    int Priority = 0,
    string? UnsupportedReason = null);

/// <param name="LocalKey">
/// Stable identity within the document. Point ids derive from it, so it must survive re-extraction —
/// a key that shifts when content is edited turns every re-ingest into a duplicate.
/// </param>
/// <param name="ImageUrl">Set when this chunk is an image; the text is then its caption and context.</param>
public sealed record ExtractedChunk(
    string LocalKey,
    string SectionPath,
    string Text,
    string? ImageUrl = null,
    string? Anchor = null);

/// <param name="SourceUpdatedAtUtc">
/// Upstream's own change marker when it publishes one. Cheaper and more reliable than hashing a body
/// whose markup churns independently of its content.
/// </param>
public sealed record ExtractedDocument(
    string? Title,
    DateTimeOffset? SourceUpdatedAtUtc,
    IReadOnlyList<ExtractedChunk> Chunks);

/// <summary>
/// Reads one family of sources into chunks. Adding a source type is one implementation plus one DI
/// registration; nothing else in the pipeline changes.
/// </summary>
public interface ISourceExtractor
{
    string SourceType { get; }

    bool CanHandle(Uri url);

    Task<Result<ExtractedDocument>> ExtractAsync(SourceDescriptor source, CancellationToken cancellationToken = default);
}

/// <summary>Picks the extractor for a URL.</summary>
public interface ISourceExtractorRegistry
{
    ISourceExtractor? Resolve(Uri url);

    ISourceExtractor? ResolveByType(string sourceType);
}

/// <summary>Enumerates the sources a family offers, for families that publish an index of their own.</summary>
public interface ISourceCatalog
{
    string SourceType { get; }

    Task<Result<IReadOnlyList<SourceDescriptor>>> ListAsync(CancellationToken cancellationToken = default);
}
