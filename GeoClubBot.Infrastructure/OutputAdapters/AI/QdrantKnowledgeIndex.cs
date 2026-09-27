using Qdrant.Client;
using Qdrant.Client.Grpc;
using UseCases.OutputPorts.AI;
using Match = Qdrant.Client.Grpc.Match;

namespace Infrastructure.OutputAdapters.AI;

/// <summary>
/// Qdrant-backed <see cref="IKnowledgeIndex"/>.
///
/// Each point carries up to two named vectors of the same width: <c>text</c> for the chunk's prose or
/// an image's caption, and <c>image</c> for the pixels. They are searched separately and merged with
/// reciprocal-rank fusion (<see cref="KnowledgeHitFusion"/>), which compares positions rather than
/// scores — necessary because text-to-text similarity sits on a visibly higher scale than
/// text-to-image, so a single blended search would rank every paragraph above every image regardless
/// of relevance.
/// </summary>
public sealed class QdrantKnowledgeIndex(QdrantClient client, string collectionName, int vectorSize)
    : IKnowledgeIndex
{
    public const string TextVectorName = "text";
    public const string ImageVectorName = "image";

    /// <summary>The written question against chunk text and captions — the <see cref="WeightedHitList.Name"/>.</summary>
    public const string QuestionTextSearch = "question → text";

    /// <summary>The written question against image pixels.</summary>
    public const string QuestionPixelSearch = "question → pixels";

    /// <summary>An attached screenshot against image pixels.</summary>
    public const string ScreenshotPixelSearch = "screenshot → pixels";

    private const uint ScrollPageSize = 256;

    /// <summary>Candidates drawn from each search before fusion; wider than the final limit so fusion has room to reorder.</summary>
    private const ulong PrefetchLimit = 40;

    /// <summary>
    /// How much a place in the question-against-pixels list counts, against the question-against-text
    /// list. Replayed on the production index with the beta testers' questions: at full weight, pictures
    /// found by their pixels alone took 53 of 144 top-8 slots. Their captions have nothing to do with
    /// the question — or they would have ranked by text — so the model can say nothing about them, and
    /// they had pushed out the very guides testers found missing: two Vietnam chunks on Khasi pines lost
    /// to three pictures of Ugandan forest. At a quarter such pictures take 13 of 144 slots.
    ///
    /// Not zero, because agreement still counts: "white car long antenna" ranks only 39th by its text
    /// but is first by its picture, and between them that is the answer.
    /// </summary>
    private const double CrossModalWeight = 0.25;

    public async Task EnsureCollectionAsync(CancellationToken cancellationToken = default)
    {
        var collections = await client.ListCollectionsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        if (collections.Any(name => name == collectionName))
        {
            return;
        }

        await client.CreateCollectionAsync(
            collectionName: collectionName,
            vectorsConfig: new VectorParamsMap
            {
                Map =
                {
                    [TextVectorName] = new VectorParams { Size = (ulong)vectorSize, Distance = Distance.Cosine },
                    [ImageVectorName] = new VectorParams { Size = (ulong)vectorSize, Distance = Distance.Cosine }
                }
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        // Filtered search without an index degrades to a full scan of the collection. The previous
        // implementation filtered on country with no index at all.
        foreach (var field in (string[])["country", "sourceType", "sourceKey", "chunkKind", "ingestRun"])
        {
            await client.CreatePayloadIndexAsync(
                collectionName: collectionName,
                fieldName: field,
                schemaType: PayloadSchemaType.Keyword,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }

    public Task UpsertAsync(
        IReadOnlyList<KnowledgePoint> points,
        string ingestRun,
        CancellationToken cancellationToken = default)
    {
        if (points.Count == 0)
        {
            return Task.CompletedTask;
        }

        var structs = points.Select(point => ToPointStruct(point, ingestRun)).ToList();
        return client.UpsertAsync(collectionName, structs, cancellationToken: cancellationToken);
    }

    public async Task<IReadOnlyList<KnowledgeHit>> SearchAsync(
        KnowledgeQuery query,
        CancellationToken cancellationToken = default) =>
        KnowledgeHitFusion.Fuse(
            await SearchListsAsync(query, cancellationToken: cancellationToken).ConfigureAwait(false),
            query.Limit);

    /// <summary>
    /// The per-vector result lists <see cref="SearchAsync"/> fuses, each carrying the weight it counts
    /// for. Public for <c>Tools/GeoClubBot.RetrievalProbe</c>, which replays questions through exactly
    /// these lists — and re-weights them, or searches approximately — to measure a change to
    /// retrieval against the real index before making it.
    /// </summary>
    /// <param name="exact">False searches the approximate index instead, for comparison only.</param>
    public async Task<IReadOnlyList<WeightedHitList>> SearchListsAsync(
        KnowledgeQuery query,
        bool exact = true,
        CancellationToken cancellationToken = default)
    {
        var filter = BuildFilter(query.Country, query.SourceType, ingestRun: null, sourceKey: null);
        var searches = new List<(QueryPoints Search, double Weight, string Name)>();

        // Length is checked as well as presence: an empty vector is never a meaningful query, and
        // sending one fails the entire request rather than just that search.
        if (query.TextVector is { Length: > 0 } textVector)
        {
            // The question against chunk text and image captions — the strongest signal by a wide
            // margin, since captions and questions are both text.
            searches.Add((BuildSearch(textVector, TextVectorName, filter, exact), 1d, QuestionTextSearch));

            // The same question against image pixels, down-weighted: see CrossModalWeight.
            searches.Add((BuildSearch(textVector, ImageVectorName, filter, exact), CrossModalWeight, QuestionPixelSearch));
        }

        // An attached screenshot against image pixels — for a "where is this?" question, the signal.
        if (query.ImageVector is { Length: > 0 } imageVector)
        {
            searches.Add((BuildSearch(imageVector, ImageVectorName, filter, exact), 1d, ScreenshotPixelSearch));
        }

        if (searches.Count == 0)
        {
            return [];
        }

        var results = await client.QueryBatchAsync(
            collectionName,
            [.. searches.Select(search => search.Search)],
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return [.. results.Select((result, index) => new WeightedHitList(
            [.. result.Result.Select(ToHit)], searches[index].Weight, searches[index].Name))];
    }

    public async Task<int> SweepAsync(
        string sourceType,
        string sourceKey,
        string ingestRun,
        CancellationToken cancellationToken = default)
    {
        // Everything for this source that the current run did not rewrite is stale by definition.
        var stale = new Filter
        {
            Must =
            {
                KeywordCondition("sourceType", sourceType),
                KeywordCondition("sourceKey", sourceKey)
            },
            MustNot = { KeywordCondition("ingestRun", ingestRun) }
        };

        var doomed = await client.ScrollAsync(
            collectionName: collectionName,
            filter: stale,
            limit: ScrollPageSize,
            payloadSelector: false,
            vectorsSelector: false,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var removed = 0;
        while (doomed.Result.Count > 0)
        {
            removed += doomed.Result.Count;
            await client.DeleteAsync(
                collectionName,
                [.. doomed.Result.Select(point => point.Id)],
                cancellationToken: cancellationToken).ConfigureAwait(false);

            // Re-scroll from the start rather than paging: the deletes above have already shifted the
            // result set, so a saved offset would skip points.
            doomed = await client.ScrollAsync(
                collectionName: collectionName,
                filter: stale,
                limit: ScrollPageSize,
                payloadSelector: false,
                vectorsSelector: false,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        return removed;
    }

    public async Task<long> CountAsync(CancellationToken cancellationToken = default)
    {
        // The collection is created on the first ingest, so before then it legitimately holds
        // nothing. Reporting that as an error made a bot with an empty index look broken.
        var collections = await client.ListCollectionsAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return collections.Any(name => name == collectionName)
            ? (long)await client.CountAsync(collectionName, cancellationToken: cancellationToken).ConfigureAwait(false)
            : 0;
    }

    public async Task<IReadOnlyList<string>> ListCountriesAsync(CancellationToken cancellationToken = default)
    {
        var countries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var payloadSelector = new WithPayloadSelector
        {
            Include = new PayloadIncludeSelector { Fields = { "country" } }
        };

        var offset = default(PointId);
        do
        {
            var page = await client.ScrollAsync(
                collectionName: collectionName,
                limit: ScrollPageSize,
                payloadSelector: payloadSelector,
                vectorsSelector: false,
                offset: offset,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            foreach (var point in page.Result)
            {
                if (point.Payload.TryGetValue("country", out var value) && !string.IsNullOrWhiteSpace(value.StringValue))
                {
                    countries.Add(value.StringValue);
                }
            }

            offset = page.NextPageOffset;
        }
        while (offset is not null);

        return [.. countries.OrderBy(country => country, StringComparer.OrdinalIgnoreCase)];
    }

    public async Task<IReadOnlyList<IndexedSourceKey>> ReadSourcesMissingImageVectorsAsync(
        CancellationToken cancellationToken = default)
    {
        var collections = await client.ListCollectionsAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (!collections.Any(name => name == collectionName))
        {
            return [];
        }

        // An image chunk is written with its image vector when embedding succeeds and without it when
        // not, so the absence of the named vector is exactly "this picture never made it in".
        var missing = new Filter
        {
            Must = { KeywordCondition("chunkKind", "image") },
            MustNot = { new Condition { HasVector = new HasVectorCondition { HasVector = ImageVectorName } } }
        };

        var payloadSelector = new WithPayloadSelector
        {
            Include = new PayloadIncludeSelector { Fields = { "sourceType", "sourceKey" } }
        };

        var sources = new HashSet<IndexedSourceKey>();
        var offset = default(PointId);
        do
        {
            var page = await client.ScrollAsync(
                collectionName: collectionName,
                filter: missing,
                limit: ScrollPageSize,
                payloadSelector: payloadSelector,
                vectorsSelector: false,
                offset: offset,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            foreach (var point in page.Result)
            {
                var sourceType = ReadString(point.Payload, "sourceType");
                var sourceKey = ReadString(point.Payload, "sourceKey");

                if (sourceType.Length > 0 && sourceKey.Length > 0)
                {
                    sources.Add(new IndexedSourceKey(sourceType, sourceKey));
                }
            }

            offset = page.NextPageOffset;
        }
        while (offset is not null);

        return [.. sources.OrderBy(source => source.SourceType, StringComparer.Ordinal)
            .ThenBy(source => source.SourceKey, StringComparer.Ordinal)];
    }

    private QueryPoints BuildSearch(ReadOnlyMemory<float> vector, string vectorName, Filter? filter, bool exact)
    {
        var search = new QueryPoints
        {
            CollectionName = collectionName,
            Query = vector.ToArray(),
            Using = vectorName,
            Limit = PrefetchLimit,
            WithPayload = new WithPayloadSelector { Enable = true },
            // Exact rather than approximate. The approximate index missed the chunk that answers
            // "is pampa's grass a real meta?" — scoring 0.372 against a tenth place of 0.359, it was
            // not among the first 300 returned. A full scan of the 35k points took 22 ms for both
            // searches on the production server, against 9 ms approximate: worth it for a question
            // that then spends seconds with the model.
            Params = new SearchParams { Exact = exact }
        };

        if (filter is not null)
        {
            search.Filter = filter;
        }

        return search;
    }

    private static PointStruct ToPointStruct(KnowledgePoint point, string ingestRun)
    {
        var chunk = point.Chunk;

        var payload = new Dictionary<string, Value>
        {
            ["sourceType"] = chunk.SourceType,
            ["sourceKey"] = chunk.SourceKey,
            ["localKey"] = chunk.LocalKey,
            ["chunkKind"] = chunk.Kind.ToString().ToLowerInvariant(),
            ["text"] = chunk.Text,
            ["sourceUrl"] = chunk.SourceUrl,
            ["imageUrl"] = chunk.ImageUrl ?? string.Empty,
            ["title"] = chunk.Title ?? string.Empty,
            // Normalised so a filter does not have to care how a source spelled it.
            ["country"] = chunk.Country?.ToLowerInvariant() ?? string.Empty,
            ["sectionPath"] = chunk.SectionPath ?? string.Empty,
            ["author"] = chunk.Author ?? string.Empty,
            ["priority"] = chunk.Priority,
            ["ingestRun"] = ingestRun
        };

        var vectors = new Dictionary<string, float[]> { [TextVectorName] = point.TextVector.ToArray() };
        if (point.ImageVector is { } imageVector)
        {
            vectors[ImageVectorName] = imageVector.ToArray();
        }

        return new PointStruct
        {
            Id = new PointId { Uuid = chunk.PointId.ToString() },
            Vectors = vectors,
            Payload = { payload }
        };
    }

    private static Filter? BuildFilter(string? country, string? sourceType, string? ingestRun, string? sourceKey)
    {
        var filter = new Filter();

        if (!string.IsNullOrWhiteSpace(country))
        {
            filter.Must.Add(KeywordCondition("country", country.ToLowerInvariant()));
        }

        if (!string.IsNullOrWhiteSpace(sourceType))
        {
            filter.Must.Add(KeywordCondition("sourceType", sourceType));
        }

        if (!string.IsNullOrWhiteSpace(sourceKey))
        {
            filter.Must.Add(KeywordCondition("sourceKey", sourceKey));
        }

        if (!string.IsNullOrWhiteSpace(ingestRun))
        {
            filter.Must.Add(KeywordCondition("ingestRun", ingestRun));
        }

        return filter.Must.Count == 0 ? null : filter;
    }

    private static Condition KeywordCondition(string field, string value) =>
        new() { Field = new FieldCondition { Key = field, Match = new Match { Keyword = value } } };

    private static KnowledgeHit ToHit(ScoredPoint point)
    {
        var payload = point.Payload;

        return new KnowledgeHit(
            Guid.TryParse(point.Id?.Uuid, out var id) ? id : Guid.Empty,
            point.Score,
            ReadString(payload, "chunkKind") == "image" ? KnowledgeChunkKind.Image : KnowledgeChunkKind.Text,
            ReadString(payload, "text"),
            ReadString(payload, "sourceUrl"),
            NullIfEmpty(ReadString(payload, "imageUrl")),
            NullIfEmpty(ReadString(payload, "title")),
            NullIfEmpty(ReadString(payload, "country")),
            NullIfEmpty(ReadString(payload, "sectionPath")),
            NullIfEmpty(ReadString(payload, "author")),
            payload.TryGetValue("priority", out var priority) ? (int)priority.IntegerValue : 0);
    }

    /// <summary>
    /// Reads a payload string defensively. A point written by an older schema version is missing keys
    /// rather than malformed, and one absent field should not throw away an otherwise good result.
    /// </summary>
    private static string ReadString(IReadOnlyDictionary<string, Value> payload, string key) =>
        payload.TryGetValue(key, out var value) ? value.StringValue ?? string.Empty : string.Empty;

    private static string? NullIfEmpty(string value) => string.IsNullOrEmpty(value) ? null : value;
}
