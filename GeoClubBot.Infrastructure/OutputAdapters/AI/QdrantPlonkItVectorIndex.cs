using Qdrant.Client;
using Qdrant.Client.Grpc;
using UseCases.OutputPorts.AI;
using Match = Qdrant.Client.Grpc.Match;

namespace Infrastructure.OutputAdapters.AI;

/// <summary>
/// Qdrant-backed implementation of <see cref="IPlonkItVectorIndex"/>. Owns the
/// "plonkit-guide" collection's lifecycle and the scroll/search query mechanics.
/// </summary>
public sealed class QdrantPlonkItVectorIndex(QdrantClient client, string collectionName = "plonkit-guide")
    : IPlonkItVectorIndex
{
    private const int VectorSize = 1024;
    private const uint ScrollPageSize = 100;

    public async Task<bool> CollectionExistsAsync(CancellationToken cancellationToken = default)
    {
        var collections = await client.ListCollectionsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        return collections.Any(c => c == collectionName);
    }

    public async Task EnsureCollectionExistsAsync(CancellationToken cancellationToken = default)
    {
        if (await CollectionExistsAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        await client.CreateCollectionAsync(
            collectionName: collectionName,
            vectorsConfig: new VectorParams { Size = VectorSize, Distance = Distance.Cosine },
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public Task DeleteCollectionAsync(CancellationToken cancellationToken = default) =>
        client.DeleteCollectionAsync(collectionName, cancellationToken: cancellationToken);

    public Task UpsertAsync(
        string id,
        ReadOnlyMemory<float> vector,
        string text,
        string source,
        string country,
        CancellationToken cancellationToken = default)
    {
        var payload = new Dictionary<string, Value>
        {
            ["text"] = text,
            ["source"] = source,
            ["country"] = country
        };

        var point = new PointStruct
        {
            Id = new PointId { Uuid = id },
            Vectors = vector.ToArray(),
            Payload = { payload }
        };

        return client.UpsertAsync(collectionName, [point], cancellationToken: cancellationToken);
    }

    public async Task<List<SectionRecord>> SearchAsync(
        ReadOnlyMemory<float> queryVector,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var results = await client.SearchAsync(
            collectionName: collectionName,
            vector: queryVector.ToArray(),
            limit: (ulong)limit,
            payloadSelector: true,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return results.Select(MapToSectionRecord).ToList();
    }

    public async Task<List<string>> GetUniqueCountriesAsync(CancellationToken cancellationToken = default)
    {
        var countries = new HashSet<string>();
        var payloadSelector = new WithPayloadSelector
        {
            Include = new PayloadIncludeSelector { Fields = { "country" } }
        };

        var scrollResponse = await client.ScrollAsync(
            collectionName: collectionName,
            limit: ScrollPageSize,
            payloadSelector: payloadSelector,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        AccumulateCountries(countries, scrollResponse);

        while (scrollResponse.NextPageOffset != null)
        {
            scrollResponse = await client.ScrollAsync(
                collectionName: collectionName,
                offset: scrollResponse.NextPageOffset,
                limit: ScrollPageSize,
                payloadSelector: payloadSelector,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            AccumulateCountries(countries, scrollResponse);
        }

        return countries.OrderBy(c => c).ToList();
    }

    public async Task<List<SectionRecord>> GetSectionsByCountryAsync(string country, CancellationToken cancellationToken = default)
    {
        var filter = new Filter
        {
            Must =
            {
                new Condition
                {
                    Field = new FieldCondition
                    {
                        Key = "country",
                        Match = new Match { Keyword = country }
                    }
                }
            }
        };

        var sections = new List<SectionRecord>();

        var scrollResponse = await client.ScrollAsync(
            collectionName: collectionName,
            filter: filter,
            limit: ScrollPageSize,
            payloadSelector: true,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        sections.AddRange(scrollResponse.Result.Select(MapToSectionRecord));

        while (scrollResponse.NextPageOffset != null)
        {
            scrollResponse = await client.ScrollAsync(
                collectionName: collectionName,
                offset: scrollResponse.NextPageOffset,
                filter: filter,
                limit: ScrollPageSize,
                payloadSelector: true,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            sections.AddRange(scrollResponse.Result.Select(MapToSectionRecord));
        }

        return sections;
    }

    private static void AccumulateCountries(HashSet<string> sink, ScrollResponse response)
    {
        foreach (var point in response.Result)
        {
            if (!point.Payload.TryGetValue("country", out var value)) continue;
            var country = value.StringValue;
            if (!string.IsNullOrEmpty(country))
            {
                sink.Add(country);
            }
        }
    }

    private static SectionRecord MapToSectionRecord(ScoredPoint result) => new()
    {
        Text = result.Payload["text"].StringValue,
        Source = result.Payload["source"].StringValue,
        Country = result.Payload["country"].StringValue
    };

    private static SectionRecord MapToSectionRecord(RetrievedPoint result) => new()
    {
        Text = result.Payload["text"].StringValue,
        Source = result.Payload["source"].StringValue,
        Country = result.Payload["country"].StringValue
    };
}
