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
    /// What a vision model said the picture shows, for an image chunk that
    /// <see cref="WantsCaption">wanted one</see> and has been described; null otherwise. Never the
    /// guide author's words, which stay in <see cref="Text"/>. Kept across a re-ingest, since the
    /// picture has not changed, and embedded with the text so a written question finds the picture.
    /// </summary>
    public string? GeneratedCaption { get; init; }

    /// <summary>The generation of <see cref="GeneratedCaption"/> (<c>AI:Captions:Version</c>); 0 when there is none.</summary>
    public int CaptionVersion { get; init; }

    /// <summary>
    /// A picture whose only words are its guide's title or a short heading: nothing a question could
    /// match, nothing an answer could say of it. Such pictures are the ones a vision model describes.
    /// Decided once here, at write time, and stored, so the describing run reads it back instead of
    /// scanning every picture's text.
    /// </summary>
    public bool WantsCaption => Kind == KnowledgeChunkKind.Image && PictureWords.IsOnlyAHeading(Text, Title, SectionPath);

    /// <summary>
    /// Derived, not stored: the same chunk always maps to the same point, so re-ingesting unchanged
    /// content updates in place rather than appending a duplicate.
    /// </summary>
    public Guid PointId => DeterministicGuid.FromName("point", $"{SourceType}|{SourceKey}|{Kind}|{LocalKey}");
}

/// <summary>
/// Whether a picture's text says anything its heading does not. Shared by the chunk written and the
/// hit read back, so what the describing run is given and what retrieval demotes are the same pictures.
/// </summary>
public static class PictureWords
{
    /// <summary>Shorter captions say nothing a model could use: "Distribution", "Summary :", "Poles :".</summary>
    public const int MinTopicalCaptionLength = 25;

    /// <summary>A section path this deep names its picture: the guide, a part of it, and the thing itself.</summary>
    public const int MinDescriptiveSectionDepth = 3;

    /// <summary>
    /// True for a picture whose only text is its guide's title or section heading — what an extractor
    /// falls back to for a picture with no caption, e.g. "Svalbard — Identifying Svalbard".
    ///
    /// Unless the heading is a path that names the picture. "A Unique USA > 7.20 Maryland (MD) > 7.20.1
    /// Signs > State Route Shield" over a picture with no other words says exactly what it shows, and it
    /// is what a member asking for Maryland's route shield should be given; "India region guessing doc >
    /// Sikkim > Sikkim taxis" likewise. A path of <see cref="MinDescriptiveSectionDepth"/> headings or
    /// more is therefore a caption, as long as it is one.
    /// </summary>
    public static bool IsOnlyAHeading(string? text, string? title, string? sectionPath)
    {
        var flat = string.Join(' ', (text ?? string.Empty).Split(default(char[]), StringSplitOptions.RemoveEmptyEntries));
        if (flat.Length < MinTopicalCaptionLength
            || flat.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            || string.Equals(flat, title, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var isItsSection = string.Equals(flat, sectionPath, StringComparison.OrdinalIgnoreCase)
                           || string.Equals(flat.Replace(" — ", " > ", StringComparison.Ordinal), sectionPath,
                               StringComparison.OrdinalIgnoreCase);

        return isItsSection
               && flat.Split(" > ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length < MinDescriptiveSectionDepth;
    }
}

/// <summary>A description a vision model wrote for a picture, as the index stores it beside the chunk.</summary>
/// <param name="Text">The description; empty when the model could not describe the picture, so it is not asked again until the version changes.</param>
/// <param name="Version">The generation it was written under (<c>AI:Captions:Version</c>).</param>
public sealed record GeneratedCaption(string Text, int Version);

/// <summary>
/// A description to write onto a picture already in the index, with the text vector that now includes
/// it. The vector is optional: without one the description is stored and shown but the picture is
/// found no better by a written question until it is embedded.
/// </summary>
public sealed record PictureDescription(Guid Id, GeneratedCaption Caption, ReadOnlyMemory<float>? TextVector = null);

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

    /// <summary>
    /// A hard filter the other way: nothing of this source type is returned. A member asking for a
    /// clue "that is not on PlonkIt" was offered nine PlonkIt tips of ten (2026-11-09).
    /// </summary>
    public string? ExcludedSourceType { get; init; }

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
    /// <summary>
    /// What a vision model said the picture shows (<see cref="KnowledgeChunk.GeneratedCaption"/>);
    /// null when nothing has described it, or it never needed describing. Empty when the model could not.
    /// </summary>
    public string? GeneratedCaption { get; init; }

    /// <summary>A generated description with words in it, ready to be shown or searched.</summary>
    public bool HasGeneratedCaption => !string.IsNullOrWhiteSpace(GeneratedCaption);

    /// <summary>
    /// A picture the model can say nothing about: its only text is a heading
    /// (<see cref="IsHeadingOnlyPicture"/>) and no description has been written for it. Where a picture
    /// is passed over or demoted for telling nothing, this is the test; a described picture tells plenty.
    /// </summary>
    public bool IsUndescribedPicture => IsHeadingOnlyPicture && !HasGeneratedCaption;

    /// <summary>
    /// The pictures of copies with the same text that were collapsed into this hit, beyond
    /// <see cref="ImageUrl"/>: best-ranked copy first, none twice, never <see cref="ImageUrl"/> itself.
    /// One Google Slides caption sits beside every picture of its slide, up to eight of them, and
    /// collapsing the copies once kept only the first: 72 % of slide pictures could never be offered.
    /// Only copies from the same link and section bring theirs, since each is shown under this hit's
    /// title and link. Empty unless <see cref="ImageUrl"/> is set.
    /// </summary>
    public IReadOnlyList<string> ExtraImageUrls { get; init; } = [];

    /// <summary>
    /// The ids of the copies collapsed into this hit, beside its own <see cref="Id"/>. Kept so that what
    /// an answer was offered can be read back whole, its extra pictures included.
    /// </summary>
    public IReadOnlyList<Guid> MergedChunkIds { get; init; } = [];

    /// <summary>
    /// The guide most players have read carries a tip like this one. Set only for a question that asks
    /// for something new (<c>MainGuideOverlap</c>), and said on the excerpt, so the model passes it over.
    /// </summary>
    public bool AlsoInMainGuide { get; init; }

    /// <summary>Every chunk this hit stands for: its own id, then its merged copies'. Never the empty id.</summary>
    public IEnumerable<Guid> ChunkIds =>
        new[] { Id }.Concat(MergedChunkIds).Where(id => id != Guid.Empty).Distinct();

    /// <summary>
    /// A picture whose only text is its guide's title or section heading — what an extractor falls
    /// back to for a picture with no caption, e.g. "Svalbard — Identifying Svalbard"
    /// (<see cref="PictureWords.IsOnlyAHeading"/>). Says nothing of whether a description has since
    /// been written for it: that is <see cref="IsUndescribedPicture"/>.
    /// </summary>
    public bool IsHeadingOnlyPicture => Kind == KnowledgeChunkKind.Image && PictureWords.IsOnlyAHeading(Text, Title, SectionPath);
}

/// <summary>A document in the index, identified the same way as its source record.</summary>
public sealed record IndexedSourceKey(string SourceType, string SourceKey);

/// <summary>What to draw at random from the index (<see cref="IKnowledgeIndex.SampleAsync"/>).</summary>
/// <param name="Limit">How many chunks to draw.</param>
/// <param name="Kind">Draw only chunks of this kind; null draws any.</param>
/// <param name="ExcludedSourceType">Draw nothing of this source type; null leaves none out.</param>
public sealed record KnowledgeSample(int Limit, KnowledgeChunkKind? Kind = null, string? ExcludedSourceType = null);

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
    /// For each of <paramref name="hits"/> that is a chunk of the index, the chunk of
    /// <paramref name="sourceType"/> whose text is nearest its own — among those filed under the hit's
    /// country, when it has one — with how near as its <see cref="KnowledgeHit.Score"/>. Keyed by the
    /// hit's id; a hit that is no chunk of the index, or has no such neighbour, is left out.
    ///
    /// Costs no embedding request: the chunk's own stored vector is what is searched by. It answers
    /// "is this clue also in that guide?" — whether a clue from a member's document is one PlonkIt has too.
    /// </summary>
    /// <param name="countrySpellings">
    /// Every spelling the index files a country under, by the spelling a hit carries. Guides spell one
    /// country differently — a document's <c>usa</c> is PlonkIt's <c>united states</c> — and compared
    /// under its own spelling alone, a document found no PlonkIt tip of its country at all. A country
    /// that is no key here is searched as the hit spells it.
    /// </param>
    Task<IReadOnlyDictionary<Guid, KnowledgeHit>> ReadNearestOfSourceAsync(
        IReadOnlyList<KnowledgeHit> hits,
        string sourceType,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? countrySpellings = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Chunks drawn at random, a different draw each time, without a score. For a question that names
    /// nothing a search could match — "tell me a clue I don't know yet" — where whatever resembles the
    /// question's wording is no better than chance, and chance at least differs the next time it is
    /// asked. Costs no embedding request.
    /// </summary>
    Task<IReadOnlyList<KnowledgeHit>> SampleAsync(KnowledgeSample sample, CancellationToken cancellationToken = default);

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

    /// <summary>
    /// Pictures that want a description (<see cref="KnowledgeChunk.WantsCaption"/>) and have none of
    /// generation <paramref name="version"/> or later, at most <paramref name="limit"/> of them, in no
    /// particular order and without a score. A picture whose description is empty counts as described:
    /// the model could not say what it shows, and asking again under the same version would get the same.
    /// </summary>
    Task<IReadOnlyList<KnowledgeHit>> ReadPicturesWantingCaptionAsync(
        int version,
        int limit,
        CancellationToken cancellationToken = default);

    /// <summary>How many pictures <see cref="ReadPicturesWantingCaptionAsync"/> would still find, for the run's report.</summary>
    Task<long> CountPicturesWantingCaptionAsync(int version, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes descriptions onto pictures already in the index, and the new text vector of each that
    /// brings one. Nothing else about the point changes; an id no longer in the index is skipped.
    /// </summary>
    Task WriteCaptionsAsync(IReadOnlyList<PictureDescription> descriptions, CancellationToken cancellationToken = default);

    /// <summary>
    /// The descriptions already written for a document's pictures, with the picture each describes, so
    /// a re-ingest of the document keeps them: the pictures have not changed, and each cost a request
    /// to describe. Pictures without a description are left out.
    /// </summary>
    Task<IReadOnlyList<StoredPictureCaption>> ReadGeneratedCaptionsAsync(
        string sourceType,
        string sourceKey,
        CancellationToken cancellationToken = default);
}

/// <summary>A description as the index holds it: on which point, and of which picture.</summary>
public sealed record StoredPictureCaption(Guid Id, string? ImageUrl, GeneratedCaption Caption);
