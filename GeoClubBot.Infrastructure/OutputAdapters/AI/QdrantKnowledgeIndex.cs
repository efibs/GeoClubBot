using Qdrant.Client;
using Qdrant.Client.Grpc;
using UseCases.OutputPorts.AI;
using Match = Qdrant.Client.Grpc.Match;

namespace Infrastructure.OutputAdapters.AI;

/// <summary>
/// Qdrant-backed <see cref="IKnowledgeIndex"/>.
///
/// Each point carries up to two named vectors of the same width: <c>text</c> for the chunk's prose or
/// an image's caption, and <c>image</c> for the pixels. They are searched as separate prefetches and
/// merged with reciprocal-rank fusion, which compares positions rather than scores — necessary because
/// text-to-text similarity sits on a visibly higher scale than text-to-image, so a single blended
/// search would rank every paragraph above every image regardless of relevance.
/// </summary>
public sealed class QdrantKnowledgeIndex(QdrantClient client, string collectionName, int vectorSize)
    : IKnowledgeIndex
{
    public const string TextVectorName = "text";
    public const string ImageVectorName = "image";

    private const uint ScrollPageSize = 256;

    /// <summary>Candidates drawn from each modality before fusion; wider than the final limit so fusion has room to reorder.</summary>
    private const ulong PrefetchLimit = 40;

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
        CancellationToken cancellationToken = default)
    {
        var filter = BuildFilter(query.Country, query.SourceType, ingestRun: null, sourceKey: null);
        var prefetches = new List<PrefetchQuery>();

        // Length is checked as well as presence: an empty vector is never a meaningful query, and
        // sending one fails the entire request rather than just that prefetch.
        if (query.TextVector is { Length: > 0 } textVector)
        {
            // The question against chunk text and image captions — the strongest signal by a wide
            // margin, since captions and questions are both text.
            prefetches.Add(BuildPrefetch(textVector, TextVectorName, filter));

            // The same question against image pixels. Cross-modal similarity is weaker in absolute
            // terms but still discriminates relevant from irrelevant, and it is the only way to reach
            // an image whose caption says little. Safe to mix in only because fusion ranks rather
            // than scores; summing these against text-to-text scores would drown them.
            prefetches.Add(BuildPrefetch(textVector, ImageVectorName, filter));
        }

        if (query.ImageVector is { Length: > 0 } imageVector)
        {
            prefetches.Add(BuildPrefetch(imageVector, ImageVectorName, filter));
        }

        if (prefetches.Count == 0)
        {
            return [];
        }

        // One prefetch needs no fusion, and querying directly preserves the true similarity score
        // instead of replacing it with a rank-derived one.
        var results = prefetches.Count == 1
            ? await client.QueryAsync(
                collectionName: collectionName,
                query: prefetches[0].Query,
                usingVector: prefetches[0].Using,
                filter: filter,
                limit: (ulong)query.Limit,
                payloadSelector: true,
                cancellationToken: cancellationToken).ConfigureAwait(false)
            : await client.QueryAsync(
                collectionName: collectionName,
                prefetch: prefetches,
                query: Fusion.Rrf,
                limit: (ulong)query.Limit,
                payloadSelector: true,
                cancellationToken: cancellationToken).ConfigureAwait(false);

        return results.Select(ToHit).ToList();
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

    private static PrefetchQuery BuildPrefetch(ReadOnlyMemory<float> vector, string vectorName, Filter? filter)
    {
        var prefetch = new PrefetchQuery
        {
            Query = vector.ToArray(),
            Using = vectorName,
            Limit = PrefetchLimit
        };

        if (filter is not null)
        {
            prefetch.Filter = filter;
        }

        return prefetch;
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
