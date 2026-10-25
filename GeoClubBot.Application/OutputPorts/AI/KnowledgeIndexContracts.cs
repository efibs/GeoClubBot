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

/// <summary>
/// Another wording of the question, embedded, and searched against chunk text as a list of its own.
/// </summary>
/// <param name="Name">Which wording it is, e.g. "conversation", for anyone explaining a result.</param>
/// <param name="Weight">How much a place in its list counts, against the lead wording's at one.</param>
public sealed record WeightedQueryVector(string Name, ReadOnlyMemory<float> Vector, double Weight);

/// <summary>
/// One search of the index. Each vector is searched as its own result list and the lists are fused by
/// rank, each counting by its weight. Everything beyond <see cref="TextVector"/>,
/// <see cref="ImageVector"/> and the hard filters defaults to adding nothing, so a bare query searches
/// exactly as it always has.
/// </summary>
public sealed record KnowledgeQuery
{
    /// <summary>
    /// The question, embedded. Matches chunk text and image captions. When there are several wordings
    /// this is the lead one: the country and key-phrase searches rank by it.
    /// </summary>
    public ReadOnlyMemory<float>? TextVector { get; init; }

    /// <summary>An attached image, embedded. Matches stored images directly.</summary>
    public ReadOnlyMemory<float>? ImageVector { get; init; }

    /// <summary>A hard filter: nothing from another country is returned. Used by <c>/ai search</c>.</summary>
    public string? Country { get; init; }

    public string? SourceType { get; init; }

    /// <summary>A hard filter on the kind of chunk: only pictures, for the answer's search tool.</summary>
    public KnowledgeChunkKind? ChunkKind { get; init; }

    public int Limit { get; init; } = 8;

    /// <summary>How much the lists searched by <see cref="TextVector"/> count.</summary>
    public double TextWeight { get; init; } = 1;

    /// <summary>How much the list searched by <see cref="ImageVector"/> counts.</summary>
    public double ImageWeight { get; init; } = 1;

    /// <summary>More wordings of the question: the one carrying its conversation, the rewritten ones.</summary>
    public IReadOnlyList<WeightedQueryVector> ExtraTextVectors { get; init; } = [];

    /// <summary>
    /// Countries the question is about, as the index spells them. Unlike <see cref="Country"/> this
    /// filters nothing: the lead wording and the screenshot are searched again within these countries,
    /// and those lists are fused with the rest.
    /// </summary>
    public IReadOnlyList<string> BoostCountries { get; init; } = [];

    /// <summary>How much a country-filtered list counts, against the same search unfiltered.</summary>
    public double CountryBoostWeight { get; init; } = 1;

    /// <summary>
    /// Literal phrases a matching chunk would contain, e.g. "no supports". Chunks containing any of
    /// them are ranked by the lead wording as one more list.
    /// </summary>
    public IReadOnlyList<string> KeyPhrases { get; init; } = [];

    /// <summary>How much the key-phrase list counts.</summary>
    public double KeyPhraseWeight { get; init; } = 1;

    /// <summary>
    /// Multiplies the fused score of a picture whose only text is a heading
    /// (<see cref="KnowledgeHit.IsHeadingOnlyPicture"/>). One leaves them where they rank.
    /// </summary>
    public double HeadingOnlyPictureWeight { get; init; } = 1;
}

/// <param name="SourceType">The chunk's extractor family, so more of its guide can be read; null when unknown.</param>
/// <param name="SourceKey">Its document within that family.</param>
/// <param name="LocalKey">Its place within the document, the order a guide's chunks are read back in.</param>
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
    int Priority,
    string? SourceType = null,
    string? SourceKey = null,
    string? LocalKey = null)
{
    /// <summary>Shorter captions say nothing a model could use: "Distribution", "Summary :", "Poles :".</summary>
    private const int MinTopicalCaptionLength = 25;

    /// <summary>
    /// The pictures of copies with the same text that were collapsed into this hit, beyond
    /// <see cref="ImageUrl"/>: best-ranked copy first, none twice, never <see cref="ImageUrl"/> itself.
    /// One Google Slides caption sits beside every picture of its slide, up to eight of them, and
    /// collapsing the copies once kept only the first: 72 % of slide pictures could never be offered.
    /// Empty unless <see cref="ImageUrl"/> is set.
    /// </summary>
    public IReadOnlyList<string> ExtraImageUrls { get; init; } = [];

    /// <summary>
    /// The ids of the copies collapsed into this hit, beside its own <see cref="Id"/>. Kept so that what
    /// an answer was offered can be read back whole, its extra pictures included.
    /// </summary>
    public IReadOnlyList<Guid> MergedChunkIds { get; init; } = [];

    /// <summary>Every chunk this hit stands for: its own id, then its merged copies'. Never the empty id.</summary>
    public IEnumerable<Guid> ChunkIds =>
        new[] { Id }.Concat(MergedChunkIds).Where(id => id != Guid.Empty).Distinct();

    /// <summary>
    /// A picture whose only text is its guide's title or section heading — what an extractor falls
    /// back to for a picture with no caption, e.g. "Svalbard — Identifying Svalbard". The model knows a
    /// picture only by its text, so it can say nothing about one of these.
    /// </summary>
    public bool IsHeadingOnlyPicture
    {
        get
        {
            if (Kind != KnowledgeChunkKind.Image)
            {
                return false;
            }

            var text = string.Join(' ', Text.Split(default(char[]), StringSplitOptions.RemoveEmptyEntries));
            return text.Length < MinTopicalCaptionLength
                   || text.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(text, Title, StringComparison.OrdinalIgnoreCase)
                   || string.Equals(text, SectionPath, StringComparison.OrdinalIgnoreCase)
                   || string.Equals(text.Replace(" — ", " > ", StringComparison.Ordinal), SectionPath,
                       StringComparison.OrdinalIgnoreCase);
        }
    }
}

/// <summary>A document in the index, identified the same way as its source record.</summary>
public sealed record IndexedSourceKey(string SourceType, string SourceKey);

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
    /// The chunks of one document, at most <paramref name="limit"/> of them, in no particular order and
    /// without a score: the rest of a guide an excerpt came from, for the answer's read-the-guide tool.
    /// Empty when the document is not in the index.
    /// </summary>
    Task<IReadOnlyList<KnowledgeHit>> ReadSourceChunksAsync(
        string sourceType,
        string sourceKey,
        int limit,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The chunks with these ids, read rather than searched, in the order asked and without a score —
    /// what an earlier answer cited, offered again with a follow-up. Copies with the same text are
    /// collapsed into one, keeping each copy's picture, as a search would offer them. Ids no longer in
    /// the index (a guide re-indexed since, a tool result that never was in it) are left out.
    /// </summary>
    Task<IReadOnlyList<KnowledgeHit>> ReadChunksAsync(
        IReadOnlyList<Guid> ids,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes points of a source that the latest run did not re-produce, identified by carrying a
    /// different <paramref name="ingestRun"/>. Deleting after writing rather than before means the
    /// index is never briefly empty, so queries stay correct throughout a re-ingest.
    /// </summary>
    Task<int> SweepAsync(string sourceType, string sourceKey, string ingestRun, CancellationToken cancellationToken = default);

    Task<long> CountAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> ListCountriesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sources holding image chunks that carry no image vector — pictures indexed but never embedded.
    /// Asked of the index because only it knows: the source registry cannot tell a source with no
    /// pictures from one whose pictures were lost.
    /// </summary>
    Task<IReadOnlyList<IndexedSourceKey>> ReadSourcesMissingImageVectorsAsync(CancellationToken cancellationToken = default);
}
